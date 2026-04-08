using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace LatticeServer.Android;

/// <summary>
/// Minimal launcher Activity for Lattice Server.
/// Shows current service status, the address clients should connect to,
/// and a Start/Stop button that controls LatticeServerService.
///
/// The web dashboard is served by Kestrel at http://localhost:5007 and
/// is accessible from the device's browser without any change to this Activity.
/// </summary>
[Activity(Label = "Lattice Server", MainLauncher = true)]
public class MainActivity : Activity
{
    private Button? _toggleButton;
    private TextView? _statusText;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Auto-start the server when the activity launches
        if (!IsServiceRunning())
        {
            StartForegroundService(new Intent(this, typeof(LatticeServerService)));
        }

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(48, 80, 48, 48);

        var title = new TextView(this) { Text = "Lattice Server", TextSize = 24f };
        title.SetTypeface(null, global::Android.Graphics.TypefaceStyle.Bold);

        var address = new TextView(this)
        {
            Text = "REST / gRPC  →  http://localhost:5007\nWeb dashboard  →  http://localhost:5007",
            TextSize = 13f,
        };

        _statusText = new TextView(this) { TextSize = 16f };

        _toggleButton = new Button(this);
        _toggleButton.Click += OnToggleClicked;

        root.AddView(title);
        root.AddView(address);
        root.AddView(_statusText);
        root.AddView(_toggleButton);

        SetContentView(root);
    }

    protected override void OnResume()
    {
        base.OnResume();
        RefreshUi();
    }

    private void OnToggleClicked(object? sender, EventArgs e)
    {
        var serviceIntent = new Intent(this, typeof(LatticeServerService));

        if (IsServiceRunning())
        {
            StopService(serviceIntent);
        }
        else
        {
            StartForegroundService(serviceIntent);
        }

        // Brief delay so the OS service state settles before we re-query it
        _toggleButton!.PostDelayed(RefreshUi, 600);
    }

    private void RefreshUi()
    {
        var running = IsServiceRunning();
        _statusText!.Text = running ? "Status: running on port 5007" : "Status: stopped";
        _toggleButton!.Text = running ? "Stop Server" : "Start Server";
    }

    private bool IsServiceRunning()
    {
        var activityManager = (ActivityManager)GetSystemService(ActivityService)!;
#pragma warning disable CA1422 // GetRunningServices is deprecated but still works for own-process services
        var services = activityManager.GetRunningServices(int.MaxValue);
#pragma warning restore CA1422
        var serviceName = Java.Lang.Class.FromType(typeof(LatticeServerService)).CanonicalName;
        return services?.Any(s => s.Service?.ClassName == serviceName) == true;
    }
}
