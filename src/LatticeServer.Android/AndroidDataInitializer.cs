namespace LatticeServer.Android;

/// <summary>
/// Copies bundled APK assets into the app's private files directory on first launch.
/// Subsequent launches are skipped via a sentinel file.
///
/// What is copied:
///   - scenarios.json → {FilesDir}/scenarios.json (default empty scenario list)
///
/// What is NOT copied:
///   - templates/ — on Android, templates come from plugin APKs via ApkTemplateSource,
///     not from a local directory.
/// </summary>
public static class AndroidDataInitializer
{
    private const string SentinelFileName = ".initialized";

    public static void EnsureInitialized()
    {
        var filesDir = global::Android.App.Application.Context.FilesDir!.AbsolutePath;
        var sentinelPath = Path.Combine(filesDir, SentinelFileName);

        if (File.Exists(sentinelPath)) return;

        Directory.CreateDirectory(filesDir);

        CopyAssetIfAbsent("scenarios.json", filesDir);

        // Write sentinel last so a crash mid-copy triggers a retry on next launch
        File.WriteAllText(sentinelPath, "1");
    }

    private static void CopyAssetIfAbsent(string assetName, string destDir)
    {
        var destPath = Path.Combine(destDir, assetName);
        if (File.Exists(destPath)) return;

        var assets = global::Android.App.Application.Context.Assets;
        if (assets == null) return;

        try
        {
            using var input = assets.Open(assetName);
            using var output = File.Create(destPath);
            input.CopyTo(output);
        }
        catch (Java.IO.FileNotFoundException)
        {
            // Asset not bundled in this APK — silently skip
        }
    }
}
