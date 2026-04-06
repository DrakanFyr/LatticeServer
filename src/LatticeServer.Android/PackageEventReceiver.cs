using Android.Content;

namespace LatticeServer.Android;

/// <summary>
/// BroadcastReceiver that listens for APK installs and uninstalls and
/// notifies ApkTemplateSource so it can hot-reload template plugins
/// without restarting the server.
///
/// Register with an IntentFilter that matches:
///   ACTION_PACKAGE_ADDED   — new APK installed or existing APK updated
///   ACTION_PACKAGE_REMOVED — APK uninstalled (PACKAGE_REPLACING is false)
///
/// The data scheme must be "package" so only package-change intents match.
///
/// The [BroadcastReceiver] attribute requires a public default constructor
/// (Android instantiates the class via reflection). Callbacks are wired
/// after construction via Init().
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public class PackageEventReceiver : BroadcastReceiver
{
    private Action<string>? _onAdded;
    private Action<string>? _onRemoved;

    // Required by Android's reflection-based instantiation.
    public PackageEventReceiver() { }

    public PackageEventReceiver(Action<string> onAdded, Action<string> onRemoved)
    {
        _onAdded = onAdded;
        _onRemoved = onRemoved;
    }

    /// <summary>
    /// Wire callbacks after default construction (e.g. when Android
    /// recreates the receiver from the manifest).
    /// </summary>
    public void Init(Action<string> onAdded, Action<string> onRemoved)
    {
        _onAdded = onAdded;
        _onRemoved = onRemoved;
    }

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action == null || intent.Data == null) return;

        var packageName = intent.Data.SchemeSpecificPart;
        if (string.IsNullOrEmpty(packageName)) return;

        switch (intent.Action)
        {
            case Intent.ActionPackageAdded:
                // ACTION_PACKAGE_ADDED fires for both fresh installs and updates.
                // For updates Android also sends ACTION_PACKAGE_REMOVED with
                // EXTRA_REPLACING=true immediately before — we ignore that removal
                // and handle the update as a new-add so the template reloads.
                _onAdded?.Invoke(packageName);
                break;

            case Intent.ActionPackageRemoved:
                // EXTRA_REPLACING=true means an update is in progress; the
                // follow-up ACTION_PACKAGE_ADDED will reload the template.
                var isReplacing = intent.GetBooleanExtra(Intent.ExtraReplacing, false);
                if (!isReplacing)
                    _onRemoved?.Invoke(packageName);
                break;
        }
    }
}
