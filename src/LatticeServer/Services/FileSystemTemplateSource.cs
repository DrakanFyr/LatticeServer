using LatticeServer.Helpers;
using LatticeServer.Models;

namespace LatticeServer.Services;

/// <summary>
/// Discovers templates from a watch directory on the local filesystem.
/// Supports both subdirectory templates (folder containing entity.json) and
/// ZIP-packaged templates (*.zip files at the watch directory root).
/// </summary>
public class FileSystemTemplateSource : ITemplateSource
{
    private readonly ILogger<FileSystemTemplateSource> _logger;
    public string WatchDirectory { get; }

    private enum SourceKind { Directory, Zip }
    private readonly Dictionary<string, SourceKind> _knownSources = new();

    private FileSystemWatcher? _watcher;
    private Timer? _rescanTimer;
    private Func<TemplateSourceEvent, Task>? _onEvent;
    private CancellationToken _cancellationToken;

    public FileSystemTemplateSource(ILogger<FileSystemTemplateSource> logger, IConfiguration configuration)
    {
        _logger = logger;
        var dataDir = configuration["DataDirectory"] ?? ".";
        if (!Path.IsPathRooted(dataDir))
            dataDir = Path.Combine(AppContext.BaseDirectory, dataDir);

        var watchDir = configuration["Templates:WatchDirectory"] ?? "templates";
        WatchDirectory = Path.IsPathRooted(watchDir)
            ? watchDir
            : Path.GetFullPath(Path.Combine(dataDir, watchDir));
    }

    public async Task StartAsync(Func<TemplateSourceEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        _onEvent = onEvent;
        _cancellationToken = cancellationToken;

        Directory.CreateDirectory(WatchDirectory);

        await ScanDirectoryAsync();

        _watcher = new FileSystemWatcher(WatchDirectory)
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
        };
        _watcher.Created += OnFileSystemChange;
        _watcher.Changed += OnFileSystemChange;
        _watcher.Deleted += OnFileSystemChange;
        _watcher.Renamed += OnFileSystemChange;

        _rescanTimer = new Timer(_ => _ = ScanDirectoryAsync(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _rescanTimer?.Dispose();
        return Task.CompletedTask;
    }

    private void OnFileSystemChange(object sender, FileSystemEventArgs e)
    {
        var relativePath = Path.GetRelativePath(WatchDirectory, e.FullPath);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        var topLevel = parts[0];

        if (string.IsNullOrEmpty(topLevel) || topLevel == ".") return;

        // Only handle direct children: subdirectories and *.zip files
        // For subdirectory changes (nested files like entity.json changed), topLevel is the dir name
        // For zip changes, topLevel is the zip filename

        string? templateId = null;
        bool isZip = false;

        if (topLevel.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && parts.Length == 1)
        {
            templateId = Path.GetFileNameWithoutExtension(topLevel);
            isZip = true;
        }
        else if (parts.Length >= 1)
        {
            // Could be a directory or a file inside a directory
            templateId = topLevel;
            isZip = false;
        }

        if (templateId == null) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(200); // brief debounce for file writes
            await HandleChangeAsync(templateId, isZip, e.ChangeType);
        });
    }

    private async Task HandleChangeAsync(string templateId, bool isZipHint, WatcherChangeTypes changeType)
    {
        if (_onEvent == null) return;

        var dirPath = Path.Combine(WatchDirectory, templateId);
        var zipPath = Path.Combine(WatchDirectory, $"{templateId}.zip");

        bool dirExists = Directory.Exists(dirPath) && File.Exists(Path.Combine(dirPath, "entity.json"));
        bool zipExists = File.Exists(zipPath);

        // Directory wins over ZIP
        if (dirExists)
        {
            var files = await ReadDirectoryTemplateAsync(dirPath);
            if (_knownSources.TryGetValue(templateId, out var existing) && existing == SourceKind.Directory)
                await _onEvent(new TemplateUpdated(templateId, files));
            else
            {
                if (zipExists)
                    _logger.LogWarning("Template '{Id}': directory shadows .zip file; .zip will be ignored.", templateId);
                _knownSources[templateId] = SourceKind.Directory;
                await _onEvent(new TemplateAdded(templateId, files));
            }
        }
        else if (zipExists)
        {
            try
            {
                var files = await ZipTemplateReader.ReadAsync(zipPath);
                if (_knownSources.TryGetValue(templateId, out var existing) && existing == SourceKind.Zip)
                    await _onEvent(new TemplateUpdated(templateId, files));
                else
                {
                    _knownSources[templateId] = SourceKind.Zip;
                    await _onEvent(new TemplateAdded(templateId, files));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template '{Id}': failed to read .zip package.", templateId);
            }
        }
        else
        {
            // Neither exists — removed
            if (_knownSources.Remove(templateId))
                await _onEvent(new TemplateRemoved(templateId));
        }
    }

    private async Task ScanDirectoryAsync()
    {
        if (!Directory.Exists(WatchDirectory)) return;
        if (_onEvent == null) return;

        var discoveredIds = new HashSet<string>(StringComparer.Ordinal);

        // Scan subdirectories
        foreach (var dir in Directory.GetDirectories(WatchDirectory))
        {
            var templateId = Path.GetFileName(dir);
            if (!File.Exists(Path.Combine(dir, "entity.json"))) continue;

            discoveredIds.Add(templateId);

            var zipPath = Path.Combine(WatchDirectory, $"{templateId}.zip");
            if (File.Exists(zipPath))
                _logger.LogWarning("Template '{Id}': directory shadows .zip file; .zip will be ignored.", templateId);

            try
            {
                var files = await ReadDirectoryTemplateAsync(dir);
                if (_knownSources.TryGetValue(templateId, out var existing) && existing == SourceKind.Directory)
                    await _onEvent(new TemplateUpdated(templateId, files));
                else
                {
                    _knownSources[templateId] = SourceKind.Directory;
                    await _onEvent(new TemplateAdded(templateId, files));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template '{Id}': failed to read directory template during scan.", templateId);
            }
        }

        // Scan .zip files (skip if same ID already covered by a directory)
        foreach (var zipFile in Directory.GetFiles(WatchDirectory, "*.zip"))
        {
            var templateId = Path.GetFileNameWithoutExtension(zipFile);
            if (discoveredIds.Contains(templateId)) continue; // directory wins

            discoveredIds.Add(templateId);

            try
            {
                var files = await ZipTemplateReader.ReadAsync(zipFile);
                if (_knownSources.TryGetValue(templateId, out var existing) && existing == SourceKind.Zip)
                    await _onEvent(new TemplateUpdated(templateId, files));
                else
                {
                    _knownSources[templateId] = SourceKind.Zip;
                    await _onEvent(new TemplateAdded(templateId, files));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Template '{Id}': failed to read .zip package during scan.", templateId);
            }
        }

        // Remove stale entries
        foreach (var key in _knownSources.Keys.ToList())
        {
            if (!discoveredIds.Contains(key))
            {
                _knownSources.Remove(key);
                await _onEvent(new TemplateRemoved(key));
            }
        }
    }

    private static async Task<RawTemplateFiles> ReadDirectoryTemplateAsync(string folderPath)
    {
        var entityJson = await File.ReadAllTextAsync(Path.Combine(folderPath, "entity.json"));

        string? configJson = File.Exists(Path.Combine(folderPath, "config.json"))
            ? await File.ReadAllTextAsync(Path.Combine(folderPath, "config.json"))
            : null;

        byte[]? behaviorDll = File.Exists(Path.Combine(folderPath, "behavior.dll"))
            ? await File.ReadAllBytesAsync(Path.Combine(folderPath, "behavior.dll"))
            : null;

        string? taskConfigJson = File.Exists(Path.Combine(folderPath, "task-configurations.json"))
            ? await File.ReadAllTextAsync(Path.Combine(folderPath, "task-configurations.json"))
            : null;

        return new RawTemplateFiles(entityJson, configJson, behaviorDll, taskConfigJson);
    }

    /// <summary>Returns true if the given template ID is sourced from a directory (not a ZIP).</summary>
    public bool IsDirectoryTemplate(string templateId) =>
        _knownSources.TryGetValue(templateId, out var kind) && kind == SourceKind.Directory;

    /// <summary>Returns true if the given template ID is sourced from a ZIP file.</summary>
    public bool IsZipTemplate(string templateId) =>
        _knownSources.TryGetValue(templateId, out var kind) && kind == SourceKind.Zip;
}
