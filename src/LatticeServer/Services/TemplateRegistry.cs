using System.Collections.Concurrent;
using System.Runtime.Loader;
using System.Text.Json;
using LatticeSDK.Templates;
using LatticeServer.Helpers;
using LatticeServer.Models;

namespace LatticeServer.Services;

/// <summary>
/// Validates and hot-loads entity templates supplied by an <see cref="ITemplateSource"/>.
/// Has no direct knowledge of the filesystem, ZIP files, or APKs — all discovery
/// is delegated to the injected source.
/// </summary>
public class TemplateRegistry : IHostedService, IDisposable
{
    private readonly ILogger<TemplateRegistry> _logger;
    private readonly ITemplateSource _source;
    private readonly ConcurrentDictionary<string, TemplateDefinition> _templates = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private readonly TaskCompletionSource _initialScanComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the initial scan performed by the template source has finished.
    /// Await this before assuming all templates are registered.
    /// </summary>
    public Task InitialScanComplete => _initialScanComplete.Task;

    public TemplateRegistry(ILogger<TemplateRegistry> logger, ITemplateSource source)
    {
        _logger = logger;
        _source = source;
    }

    public IEnumerable<TemplateDefinition> GetAll() => _templates.Values;

    public bool TryGet(string templateId, out TemplateDefinition definition)
        => _templates.TryGetValue(templateId, out definition!);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            await _source.StartAsync(OnTemplateEvent, cancellationToken);
            _initialScanComplete.TrySetResult();
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => _source.StopAsync(cancellationToken);

    public void Dispose()
    {
        _loadLock.Dispose();
    }

    private async Task OnTemplateEvent(TemplateSourceEvent evt)
    {
        await _loadLock.WaitAsync();
        try
        {
            switch (evt)
            {
                case TemplateAdded added:
                    await LoadTemplateCoreAsync(added.TemplateId, added.Files);
                    break;
                case TemplateUpdated updated:
                    UnloadTemplate(updated.TemplateId);
                    await LoadTemplateCoreAsync(updated.TemplateId, updated.Files);
                    break;
                case TemplateRemoved removed:
                    UnloadTemplate(removed.TemplateId);
                    break;
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task LoadTemplateCoreAsync(string templateId, RawTemplateFiles files)
    {
        string rawEntityJson = files.EntityJson;

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
        if (files.ConfigJson != null)
        {
            try
            {
                config = ParseConfig(templateId, files.ConfigJson);
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

        if (files.BehaviorDll != null)
        {
            var tempDllPath = Path.Combine(Path.GetTempPath(), $"lattice_template_{templateId}_{Guid.NewGuid()}.dll");
            try
            {
                await File.WriteAllBytesAsync(tempDllPath, files.BehaviorDll);

                loadContext = new AssemblyLoadContext($"template:{templateId}", isCollectible: true);
                loadContext.Unloading += _ =>
                {
                    try { File.Delete(tempDllPath); } catch { }
                };

                var assembly = loadContext.LoadFromAssemblyPath(tempDllPath);
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

        // Validate task-configurations.json if present
        string? rawTaskConfigurationsJson = null;
        if (files.TaskConfigurationsJson != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(files.TaskConfigurationsJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("task-configurations.json must be a JSON array.");
                rawTaskConfigurationsJson = files.TaskConfigurationsJson;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Template '{Id}': failed to parse task-configurations.json; overrides ignored.", templateId);
            }
        }

        var definition = new TemplateDefinition(
            TemplateId: templateId,
            SourcePath: null,
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

    private void UnloadTemplate(string templateId)
    {
        if (_templates.TryRemove(templateId, out var old))
        {
            ProtobufJsonConverter.UnregisterTypes(templateId);
            old.LoadContext?.Unload();
            _logger.LogInformation("Template '{Id}' removed.", templateId);
        }
    }

    private static LatticeSDK.Templates.TemplateConfig ParseConfig(string templateId, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

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
    string? SourcePath,
    string RawEntityJson,
    LatticeSDK.Templates.TemplateConfig Config,
    Type? BehaviorType,
    AssemblyLoadContext? LoadContext,
    IReadOnlyList<Google.Protobuf.Reflection.MessageDescriptor> CustomTaskDescriptors,
    string? RawTaskConfigurationsJson);
