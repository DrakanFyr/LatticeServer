using System.Collections.Concurrent;
using System.Runtime.Loader;
using System.Text.Json;
using LatticeSDK.Templates;
using LatticeServer.Helpers;

namespace LatticeServer.Services;

/// <summary>
/// Discovers, validates, and hot-loads entity templates from a watch directory.
/// Each template is a folder whose name is the template ID. The folder contains
/// entity.json (required), config.json (optional), and behavior.dll (optional).
/// </summary>
public class TemplateRegistry : IHostedService, IDisposable
{
    private readonly ILogger<TemplateRegistry> _logger;
    private readonly string _watchDir;
    private readonly ConcurrentDictionary<string, TemplateDefinition> _templates = new();

    private FileSystemWatcher? _watcher;
    private Timer? _rescanTimer;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly TaskCompletionSource _initialScanComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the initial directory scan has finished.
    /// Await this before assuming all on-disk templates are registered.
    /// </summary>
    public Task InitialScanComplete => _initialScanComplete.Task;

    public TemplateRegistry(ILogger<TemplateRegistry> logger, IConfiguration configuration)
    {
        _logger = logger;
        var dataDir = configuration["DataDirectory"] ?? ".";
        var watchDir = configuration["Templates:WatchDirectory"] ?? "templates";
        _watchDir = Path.IsPathRooted(watchDir)
            ? watchDir
            : Path.GetFullPath(Path.Combine(dataDir, watchDir));
    }

    public IEnumerable<TemplateDefinition> GetAll() => _templates.Values;

    public bool TryGet(string templateId, out TemplateDefinition definition)
        => _templates.TryGetValue(templateId, out definition!);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_watchDir);

        // Initial scan — signal completion when done
        _ = Task.Run(async () =>
        {
            await ScanDirectoryAsync();
            _initialScanComplete.TrySetResult();
        }, cancellationToken);

        // FileSystemWatcher for live changes
        _watcher = new FileSystemWatcher(_watchDir)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnFileSystemChange;
        _watcher.Changed += OnFileSystemChange;
        _watcher.Deleted += OnFileSystemChange;
        _watcher.Renamed += OnFileSystemChange;

        // 30-second periodic rescan as fallback
        _rescanTimer = new Timer(_ => _ = ScanDirectoryAsync(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _rescanTimer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _rescanTimer?.Dispose();
        _loadLock.Dispose();
    }

    private void OnFileSystemChange(object sender, FileSystemEventArgs e)
    {
        // Determine which template folder was affected
        var relativePath = Path.GetRelativePath(_watchDir, e.FullPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        var templateId = parts[0];

        if (string.IsNullOrEmpty(templateId) || templateId == ".") return;

        var folderPath = Path.Combine(_watchDir, templateId);

        if (e.ChangeType == WatcherChangeTypes.Deleted && !Directory.Exists(folderPath))
        {
            // Whole template folder deleted
            if (_templates.TryRemove(templateId, out var old))
            {
                ProtobufJsonConverter.UnregisterTypes(templateId);
                old.LoadContext?.Unload();
                _logger.LogInformation("Template '{Id}' removed (folder deleted).", templateId);
            }
            return;
        }

        // Re-load the affected template
        _ = Task.Run(async () =>
        {
            await Task.Delay(200); // brief debounce for file writes
            await LoadTemplateAsync(templateId, folderPath);
        });
    }

    private async Task ScanDirectoryAsync()
    {
        if (!Directory.Exists(_watchDir)) return;

        var folders = Directory.GetDirectories(_watchDir);
        foreach (var folder in folders)
        {
            var templateId = Path.GetFileName(folder);
            await LoadTemplateAsync(templateId, folder);
        }

        // Remove stale entries (folders that no longer exist)
        foreach (var key in _templates.Keys)
        {
            if (!Directory.Exists(Path.Combine(_watchDir, key)))
            {
                if (_templates.TryRemove(key, out var old))
                {
                    ProtobufJsonConverter.UnregisterTypes(key);
                    old.LoadContext?.Unload();
                    _logger.LogInformation("Template '{Id}' removed (folder gone on rescan).", key);
                }
            }
        }
    }

    private async Task LoadTemplateAsync(string templateId, string folderPath)
    {
        await _loadLock.WaitAsync();
        try
        {
            await LoadTemplateCoreAsync(templateId, folderPath);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadTemplateCoreAsync(string templateId, string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            if (_templates.TryRemove(templateId, out var old))
            {
                ProtobufJsonConverter.UnregisterTypes(templateId);
                old.LoadContext?.Unload();
                _logger.LogInformation("Template '{Id}' removed.", templateId);
            }
            return;
        }

        var entityJsonPath = Path.Combine(folderPath, "entity.json");
        if (!File.Exists(entityJsonPath))
        {
            _logger.LogError("Template '{Id}': entity.json not found in '{Folder}'. Template rejected.", templateId, folderPath);
            return;
        }

        string rawEntityJson;
        try
        {
            rawEntityJson = await File.ReadAllTextAsync(entityJsonPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template '{Id}': failed to read entity.json.", templateId);
            return;
        }

        // Dry-run: validate token substitution + proto parse
        try
        {
            var substituted = TokenSubstitutor.Substitute(rawEntityJson);
            ProtobufJsonConverter.FromJson<Anduril.Entitymanager.V1.Entity>(substituted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template '{Id}': entity.json failed validation (token substitution or proto parse). Template rejected.", templateId);
            return;
        }

        // Parse config.json if present
        var config = DefaultTemplateConfig();
        var configJsonPath = Path.Combine(folderPath, "config.json");
        if (File.Exists(configJsonPath))
        {
            try
            {
                var configJson = await File.ReadAllTextAsync(configJsonPath);
                config = ParseConfig(templateId, configJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template '{Id}': failed to parse config.json; using defaults.", templateId);
            }
        }

        if (string.IsNullOrWhiteSpace(config.DisplayName))
        {
            _logger.LogWarning("Template '{Id}': no displayName set in config.json. The template ID will be shown in the UI.", templateId);
        }

        // Unload previous context if reloading (must happen before RegisterTypes
        // so the new registration isn't immediately removed by UnregisterTypes)
        if (_templates.TryGetValue(templateId, out var existing))
        {
            ProtobufJsonConverter.UnregisterTypes(templateId);
            existing.LoadContext?.Unload();
        }

        // Load behavior DLL if present
        Type? behaviorType = null;
        AssemblyLoadContext? loadContext = null;
        var customDescriptors = new List<Google.Protobuf.Reflection.MessageDescriptor>();
        var behaviorDllPath = Path.Combine(folderPath, "behavior.dll");
        if (File.Exists(behaviorDllPath))
        {
            try
            {
                loadContext = new AssemblyLoadContext($"template:{templateId}", isCollectible: true);
                var assembly = loadContext.LoadFromAssemblyPath(behaviorDllPath);
                var candidates = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ITaskableEntity).IsAssignableFrom(t))
                    .ToList();

                // Scan for ICustomTaskTypes implementations
                var customTaskTypesCandidates = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface && typeof(ICustomTaskTypes).IsAssignableFrom(t))
                    .ToList();

                foreach (var candidateType in customTaskTypesCandidates)
                {
                    try
                    {
                        var instance = (ICustomTaskTypes)Activator.CreateInstance(candidateType)!;
                        customDescriptors.AddRange(instance.GetTaskDescriptors());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Template '{Id}': failed to instantiate ICustomTaskTypes implementor '{Type}'.", templateId, candidateType.FullName);
                    }
                }

                if (customDescriptors.Count > 0)
                {
                    ProtobufJsonConverter.RegisterTypes(templateId, customDescriptors);
                    _logger.LogInformation("Template '{Id}': registered {Count} custom task descriptor(s).", templateId, customDescriptors.Count);
                }

                // Determine ITaskableEntity behavior type
                if (candidates.Count > 1)
                {
                    _logger.LogWarning("Template '{Id}': behavior.dll has {Count} ITaskableEntity implementations (ambiguous). Treating as non-taskable.", templateId, candidates.Count);
                }
                else if (candidates.Count == 1)
                {
                    behaviorType = candidates[0];
                    _logger.LogInformation("Template '{Id}': loaded behavior type '{Type}'.", templateId, behaviorType.FullName);
                }

                bool hasTaskable = candidates.Count == 1;
                bool hasCustomTypes = customTaskTypesCandidates.Count > 0;

                if (!hasTaskable && !hasCustomTypes)
                {
                    _logger.LogWarning("Template '{Id}': behavior.dll has no ITaskableEntity or ICustomTaskTypes. Unloading.", templateId);
                    loadContext.Unload();
                    loadContext = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template '{Id}': failed to load behavior.dll.", templateId);
                loadContext?.Unload();
                loadContext = null;
            }
        }

        // Load task-configurations.json if present
        string? rawTaskConfigurationsJson = null;
        var taskConfigurationsPath = Path.Combine(folderPath, "task-configurations.json");
        if (File.Exists(taskConfigurationsPath))
        {
            try
            {
                rawTaskConfigurationsJson = await File.ReadAllTextAsync(taskConfigurationsPath);
                using var doc = JsonDocument.Parse(rawTaskConfigurationsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("task-configurations.json must be a JSON array.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template '{Id}': failed to parse task-configurations.json; overrides ignored.", templateId);
                rawTaskConfigurationsJson = null;
            }
        }

        var definition = new TemplateDefinition(
            TemplateId: templateId,
            FolderPath: folderPath,
            RawEntityJson: rawEntityJson,
            Config: config,
            BehaviorType: behaviorType,
            LoadContext: loadContext,
            CustomTaskDescriptors: customDescriptors,
            RawTaskConfigurationsJson: rawTaskConfigurationsJson);

        _templates[templateId] = definition;

        _logger.LogInformation(
            "Template '{Id}' loaded (taskable={Taskable}, tickIntervalMs={Tick}).",
            templateId, behaviorType != null, config.TickIntervalMs);
    }

    private static LatticeSDK.Templates.TemplateConfig ParseConfig(string templateId, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Warn if id field mismatches folder name
        if (root.TryGetProperty("id", out var idEl))
        {
            var configId = idEl.GetString();
            if (configId != null && configId != templateId)
            {
                // Can't log here easily without a logger reference; caller logs via catch
            }
        }

        SpawnLocation? defaultLocation = null;
        if (root.TryGetProperty("defaultLocation", out var locEl))
        {
            var lat = locEl.TryGetProperty("latitudeDegrees", out var latEl) ? latEl.GetDouble() : 0;
            var lon = locEl.TryGetProperty("longitudeDegrees", out var lonEl) ? lonEl.GetDouble() : 0;
            double? alt = locEl.TryGetProperty("altitudeHaeMeters", out var altEl) ? altEl.GetDouble() : null;
            defaultLocation = new SpawnLocation(lat, lon, alt);
        }

        var tickMs = 1000;
        if (root.TryGetProperty("tickIntervalMs", out var tickEl))
        {
            tickMs = Math.Max(32, tickEl.GetInt32());
        }

        string? category = null;
        if (root.TryGetProperty("category", out var categoryEl))
        {
            category = categoryEl.GetString();
        }

        string? displayName = null;
        if (root.TryGetProperty("displayName", out var displayNameEl))
        {
            displayName = displayNameEl.GetString();
        }

        var custom = new Dictionary<string, JsonElement>();
        if (root.TryGetProperty("custom", out var customEl) && customEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in customEl.EnumerateObject())
            {
                custom[prop.Name] = prop.Value.Clone();
            }
        }

        return new LatticeSDK.Templates.TemplateConfig(defaultLocation, tickMs, category, displayName, custom);
    }

    private static LatticeSDK.Templates.TemplateConfig DefaultTemplateConfig() =>
        new(DefaultLocation: null, TickIntervalMs: 1000, Category: null, DisplayName: null,
            Custom: new Dictionary<string, JsonElement>());
}

/// <summary>Represents a loaded template definition.</summary>
public record TemplateDefinition(
    string TemplateId,
    string FolderPath,
    string RawEntityJson,
    LatticeSDK.Templates.TemplateConfig Config,
    Type? BehaviorType,
    AssemblyLoadContext? LoadContext,
    IReadOnlyList<Google.Protobuf.Reflection.MessageDescriptor> CustomTaskDescriptors,
    string? RawTaskConfigurationsJson);
