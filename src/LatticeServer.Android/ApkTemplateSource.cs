using Android.Content;
using Android.Content.PM;
using Microsoft.Extensions.Logging;
using LatticeServer.Helpers;
using LatticeServer.Services;

namespace LatticeServer.Android;

/// <summary>
/// Discovers Lattice template plugins from installed APKs via Android's PackageManager.
///
/// Plugin APKs declare themselves with two manifest meta-data entries:
///   com.lattice.template      = "true"   (discovery flag)
///   com.lattice.template.id   = "my-id"  (template ID; falls back to package name)
///
/// Template files live in the APK's assets/ directory:
///   assets/entity.json              (required)
///   assets/config.json              (optional)
///   assets/behavior.dll             (optional)
///   assets/task-configurations.json (optional)
///
/// ZipTemplateReader.ReadAsync(stream, entryPrefix: "assets/") handles extraction
/// since an APK is a ZIP archive.
///
/// Hot-reload: a BroadcastReceiver listens for ACTION_PACKAGE_ADDED and
/// ACTION_PACKAGE_REMOVED to pick up plugin installs/uninstalls without restarting.
/// </summary>
public class ApkTemplateSource : ITemplateSource
{
    private const string MetaDataFlag = "com.lattice.template";
    private const string MetaDataId = "com.lattice.template.id";

    private readonly ILogger<ApkTemplateSource> _logger;
    private readonly Context _context;
    private readonly PackageManager _packageManager;

    // package name → template ID, used for uninstall lookup
    private readonly Dictionary<string, string> _packageToTemplateId = new();

    private Func<TemplateSourceEvent, Task>? _onEvent;
    private PackageEventReceiver? _receiver;

    public ApkTemplateSource(ILogger<ApkTemplateSource> logger)
    {
        _logger = logger;
        _context = global::Android.App.Application.Context;
        _packageManager = _context.PackageManager
            ?? throw new InvalidOperationException("PackageManager is unavailable.");
    }

    public async Task StartAsync(Func<TemplateSourceEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        _onEvent = onEvent;

        // Register for install/uninstall broadcasts before the scan so we
        // don't miss a package that arrives during the startup scan.
        RegisterReceiver();

        await ScanInstalledPackagesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_receiver != null)
        {
            try { _context.UnregisterReceiver(_receiver); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to unregister PackageEventReceiver."); }
            _receiver = null;
        }

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Startup scan
    // -------------------------------------------------------------------------

    private async Task ScanInstalledPackagesAsync(CancellationToken cancellationToken)
    {
        IList<ApplicationInfo> packages;
        try
        {
#pragma warning disable CA1422 // GetInstalledApplications is the correct API for our target SDK
            packages = _packageManager.GetInstalledApplications(PackageInfoFlags.MetaData)
                       ?? [];
#pragma warning restore CA1422
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query installed packages.");
            return;
        }

        foreach (var appInfo in packages)
        {
            if (cancellationToken.IsCancellationRequested) break;

            if (!IsLatticePlugin(appInfo)) continue;

            var templateId = GetTemplateId(appInfo);
            await LoadPluginAsync(templateId, appInfo.SourceDir!, appInfo.PackageName!);
        }
    }

    // -------------------------------------------------------------------------
    // BroadcastReceiver registration
    // -------------------------------------------------------------------------

    private void RegisterReceiver()
    {
        _receiver = new PackageEventReceiver(
            onAdded: packageName => _ = HandlePackageAddedAsync(packageName),
            onRemoved: packageName => _ = HandlePackageRemovedAsync(packageName));

        var filter = new IntentFilter();
        filter.AddAction(Intent.ActionPackageAdded);
        filter.AddAction(Intent.ActionPackageRemoved);
        filter.AddDataScheme("package");

        _context.RegisterReceiver(_receiver, filter);
    }

    // -------------------------------------------------------------------------
    // Hot-reload handlers
    // -------------------------------------------------------------------------

    private async Task HandlePackageAddedAsync(string packageName)
    {
        ApplicationInfo appInfo;
        try
        {
#pragma warning disable CA1422
            appInfo = _packageManager.GetApplicationInfo(packageName, PackageInfoFlags.MetaData);
#pragma warning restore CA1422
        }
        catch (PackageManager.NameNotFoundException)
        {
            return; // race: package removed before we could query it
        }

        if (!IsLatticePlugin(appInfo)) return;

        var templateId = GetTemplateId(appInfo);

        // If we already know this template (package update), emit Removed first
        // so TemplateRegistry unloads the old assembly before we load the new one.
        if (_packageToTemplateId.TryGetValue(packageName, out var oldTemplateId))
        {
            _logger.LogInformation("Plugin package updated: {Package} (template: {TemplateId})", packageName, oldTemplateId);
            await EmitAsync(new TemplateRemoved(oldTemplateId));
            _packageToTemplateId.Remove(packageName);
        }

        await LoadPluginAsync(templateId, appInfo.SourceDir!, packageName);
    }

    private async Task HandlePackageRemovedAsync(string packageName)
    {
        if (!_packageToTemplateId.TryGetValue(packageName, out var templateId)) return;

        _logger.LogInformation("Plugin package removed: {Package} (template: {TemplateId})", packageName, templateId);
        _packageToTemplateId.Remove(packageName);
        await EmitAsync(new TemplateRemoved(templateId));
    }

    // -------------------------------------------------------------------------
    // Plugin loading
    // -------------------------------------------------------------------------

    private async Task LoadPluginAsync(string templateId, string apkPath, string packageName)
    {
        _logger.LogInformation("Loading template plugin: {TemplateId} from {ApkPath}", templateId, apkPath);

        try
        {
            await using var stream = new FileStream(apkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var files = await ZipTemplateReader.ReadAsync(stream, entryPrefix: "assets/");

            _packageToTemplateId[packageName] = templateId;
            await EmitAsync(new TemplateAdded(templateId, files));
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning("Plugin APK {ApkPath} is missing entity.json — skipping. ({Message})", apkPath, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load template plugin from {ApkPath}.", apkPath);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool IsLatticePlugin(ApplicationInfo appInfo)
        => appInfo.MetaData?.ContainsKey(MetaDataFlag) == true;

    private static string GetTemplateId(ApplicationInfo appInfo)
    {
        var id = appInfo.MetaData?.GetString(MetaDataId);
        return string.IsNullOrWhiteSpace(id) ? appInfo.PackageName! : id;
    }

    private Task EmitAsync(TemplateSourceEvent evt)
        => _onEvent?.Invoke(evt) ?? Task.CompletedTask;
}
