using Microsoft.Extensions.Configuration;

namespace LatticeServer.Android;

/// <summary>
/// Injects Android-specific configuration overrides into the host builder:
/// sets DataDirectory to the app's private files directory, and configures
/// Kestrel for h2c (HTTP/2 cleartext) on port 5007 with HTTPS disabled.
///
/// Call builder.Configuration.AddAndroidDefaults() inside the configure
/// callback passed to Program.CreateLatticeApp().
/// </summary>
public static class AndroidAppConfig
{
    public static IConfigurationBuilder AddAndroidDefaults(this IConfigurationBuilder builder)
    {
        var filesDir = global::Android.App.Application.Context.FilesDir!.AbsolutePath;

        return builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // Store all server data (templates, scenarios) in the app's private files directory
            ["DataDirectory"] = filesDir,

            // h2c: HTTP/2 cleartext on localhost. TLS is unnecessary for loopback traffic
            // and avoids certificate provisioning complexity on Android.
            ["Kestrel:Endpoints:Http:Url"]       = "http://0.0.0.0:5007",
            ["Kestrel:Endpoints:Http:Protocols"]  = "Http1AndHttp2",

            // Disable the HTTPS endpoint — no certificate available on Android
            ["Kestrel:Endpoints:Https:Url"]       = "",
        });
    }
}
