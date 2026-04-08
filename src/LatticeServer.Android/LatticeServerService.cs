using Android.App;
using Android.Content;
using Android.OS;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using LatticeServer.Services;

namespace LatticeServer.Android;

/// <summary>
/// Android foreground service that hosts the Kestrel web server.
///
/// StartCommandResult.Sticky causes Android to restart the service automatically
/// if it is killed due to memory pressure, preserving the "always-on server" behavior.
///
/// The notification keeps the process alive and satisfies Android's foreground
/// service requirements. Users can tap it to return to MainActivity.
/// </summary>
[Service(Exported = false, ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeSpecialUse)]
public class LatticeServerService : Service
{
    private WebApplication? _app;
    private const int NotificationId = 1001;
    private const string ChannelId = "lattice_server";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        // Must be set before any HTTP client is created; enables gRPC over h2c (HTTP/2 cleartext)
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        // Supress the hosting startup assembly discovery which fails on Android
        // with an empty assembly name from the environment.
        System.Environment.SetEnvironmentVariable("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES", null);

        EnsureNotificationChannel();
        StartForeground(NotificationId, BuildNotification("Lattice Server running on port 5007"));

        Task.Run(async () =>
        {
            try
            {
                AndroidDataInitializer.EnsureInitialized();

                _app = LatticeServer.LatticeApp.CreateApp([], builder =>
                {
                    // Inject Android-specific paths.
                    builder.Configuration.AddAndroidDefaults();

                    // Serve the web dashboard from the copied wwwroot in FilesDir
                    var filesDir = global::Android.App.Application.Context.FilesDir!.AbsolutePath;
                    builder.Environment.WebRootPath = Path.Combine(filesDir, "wwwroot");

                    // Configure Kestrel programmaically for h2c (HTTP/2 cleartext).
                    // This overrides config-based endpoints (including the HTTPS endpoint
                    // from appsettings.json that requires a certificate unavailable on Android).
                    builder.WebHost.ConfigureKestrel(options =>
                    {
                        options.ListenAnyIP(5007, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        });
                    });

                    // Register the Android APK plugin source. TryAddSingleton in
                    // CreateLatticeApp will skip FileSystemTemplateSource because
                    // ITemplateSource is already registered here.
                    builder.Services.AddSingleton<ITemplateSource, ApkTemplateSource>();
                });

                await _app.RunAsync();
            }
            catch (Exception ex)
            {
                global::Android.Util.Log.Error("LatticeServer", $"Server crashed: {ex}");
                StopSelf();
            }
        });

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        _app?.StopAsync(cts.Token).GetAwaiter().GetResult();
        base.OnDestroy();
    }

    private void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;

        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) != null) return;

        var channel = new NotificationChannel(ChannelId, "Lattice Server", NotificationImportance.Low)
        {
            Description = "Indicates the Lattice Server is running in the background"
        };
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(string contentText)
    {
        var tapIntent = new Intent(this, typeof(MainActivity));
        tapIntent.SetFlags(ActivityFlags.SingleTop);
        var pendingIntent = PendingIntent.GetActivity(
            this, 0, tapIntent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        return new Notification.Builder(this, ChannelId)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetContentTitle("Lattice Server")
            .SetContentText(contentText)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build()!;
    }
}
