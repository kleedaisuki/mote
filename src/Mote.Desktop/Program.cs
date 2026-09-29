using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Mote.Engine;
using Mote.Formats;
using Mote.Telemetry;
using Mote.Configuration;
using Mote.Themes;

namespace Mote.Desktop;

/// <summary>Creates the desktop application without runtime assembly scanning.</summary>
internal static class Program
{
    /// <summary>Whether this process should exercise and close its first UI window.</summary>
    internal static bool IsUiSmoke { get; private set; }
    private static bool UiSmokeReachedLayout { get; set; }
    /// <summary>Effective settings loaded once before desktop composition.</summary>
    internal static MoteConfiguration Configuration { get; private set; } = null!;
    /// <summary>Resolved compile-time theme policy selected before constructing a window.</summary>
    internal static IThemePolicy Theme { get; set; } = ThemePolicies.Get(ThemePolicies.DefaultId);

    /// <summary>Starts the native desktop lifetime and forwards file arguments.</summary>
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--check-runtime")
        {
            using var document = new Document("{\"mote\":true}");
            var analysis = DocumentPolicies.ForKind(DocumentKind.Json)
                .Analyze(document.Snapshot.GetText());
            if (analysis.Diagnostics.Count != 0) return 1;
            Console.WriteLine("mote runtime ok");
            return 0;
        }

        IsUiSmoke = args.Length == 1 && args[0] == "--smoke-ui";
        Configuration = MoteConfigLoader.Load();
        if (Configuration.TraceEnabled)
            MoteTelemetry.Configure(new TelemetryOptions
            {
                Enabled = true,
                OutputDirectory = Configuration.TraceDirectory
            });
        else
            MoteTelemetry.ConfigureFromEnvironment(Configuration.TraceDirectory);
        StartupScope = MoteTelemetry.Start(TelemetryOperation.Startup);
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            if (!IsUiSmoke) return 0;
            Console.WriteLine(UiSmokeReachedLayout ? "mote ui ok" : "mote ui failed");
            return UiSmokeReachedLayout ? 0 : 1;
        }
        finally
        {
            CompleteStartup();
            MoteTelemetry.ShutdownAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>Configures the platform backend and system fonts.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static TelemetryScope? StartupScope { get; set; }

    /// <summary>Closes the cold-start trace when the first editor window is shown.</summary>
    internal static void CompleteStartup()
    {
        StartupScope?.Dispose();
        StartupScope = null;
    }

    /// <summary>
    /// Waits for a positive-size window at render priority, then lets the event
    /// loop run briefly before graceful close. This detects XAML/control startup
    /// failures without requiring UI automation or synthesizing keyboard input.
    /// It does not prove that pixels reached the physical display.
    /// </summary>
    internal static void ScheduleUiSmokeCompletion(Window window) =>
        Dispatcher.UIThread.Post(() =>
        {
            UiSmokeReachedLayout = window.Bounds.Width > 0 && window.Bounds.Height > 0;
            DispatcherTimer.RunOnce(window.Close, TimeSpan.FromMilliseconds(250));
        }, DispatcherPriority.Render);
}
