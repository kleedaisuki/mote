using Mote.Configuration;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Native;

/// <summary>Native AOT entry point and static platform composition.</summary>
internal static class Program
{
    /// <summary>Starts one OS-native single-document editor process.</summary>
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--check-runtime")
        {
            Console.WriteLine("mote-native-ready");
            return 0;
        }
        if (args.Length == 1 && args[0] == "--check-native-windows-canvas")
            return CheckWindowsCanvas();
        if (args.Length == 1 && args[0] == "--check-native-mac-canvas")
            return CheckMacCanvas();
        if (args.Length == 1 && args[0] == "--check-native-mac-flow-rendering")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.MacFlowRenderingProbe.Run();
            if (result == 0) Console.WriteLine("mote-native-mac-flow-rendering-ready");
            return result;
        }
        if (args.Length == 1 && args[0] == "--check-native-mac-theme-overrides")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.MacThemeOverrideProbe.Run();
            if (result == 0) Console.WriteLine("mote-native-mac-theme-overrides-ready");
            return result;
        }
        if (args.Length == 5 && args[0] == "--check-native-canvas-window")
            return CheckCanvasWindow(args[1], args[2], args[3], args[4]);
        if (args.Length == 3 && args[0] == "--check-native-mac-canvas-clipboard")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCanvasClipboardProbe.Run(args[1], args[2]);
            if (result == 0) Console.WriteLine("mote-native-mac-canvas-clipboard-ready");
            return result;
        }
        if (args.Length == 2 && args[0] == "--check-native-mac-canvas-delete")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCanvasDeleteProbe.Run(args[1]);
            if (result == 0) Console.WriteLine("mote-native-mac-canvas-delete-ready");
            return result;
        }
        if (args.Length == 2 && args[0] == "--check-native-mac-canvas-ax")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCanvasAccessibilityProbe.Run(args[1]);
            if (result == 0) Console.WriteLine("mote-native-mac-canvas-ax-ready");
            return result;
        }
        if (args.Length == 4 && args[0] == "--check-native-mac-horizontal")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCanvasHorizontalProbe.Run(args[1], args[2], args[3]);
            if (result == 0) Console.WriteLine("mote-native-mac-horizontal-ready");
            return result;
        }
        if (args.Length == 4 && args[0] == "--check-native-mac-theme")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCanvasThemeProbe.Run(args[1], args[2], args[3]);
            if (result == 0) Console.WriteLine($"mote-native-mac-theme-ready mode={args[3]}");
            return result;
        }
        if (args.Length == 4 && args[0] == "--check-native-mac-composition-theme")
        {
            if (!OperatingSystem.IsMacOS()) return 3;
            var result = Mac.Canvas.MacCompositionThemeProbe.Run(args[1], args[2], args[3]);
            if (result == 0)
                Console.WriteLine($"mote-native-mac-composition-theme-ready mode={args[3]}");
            return result;
        }
        if (args.Length == 3 && args[0] == "--check-native-mac-workflow")
        {
            if (!OperatingSystem.IsMacOS())
            {
                Console.Error.WriteLine("This native workflow probe requires macOS.");
                return 3;
            }
            var result = Mac.MacNativeWorkflowProbe.Run(args[1], args[2]);
            if (result == 0) Console.WriteLine("mote-native-mac-workflow-ready");
            return result;
        }
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.WriteLine("Usage: mote [path] | mote --legacy-page [path]");
            Console.WriteLine("Default: continuous source-backed editor (under validation); --legacy-page restores the established page view.");
            Console.WriteLine("GUI startup diagnostic: mote [--legacy-page] --smoke-gui");
            Console.WriteLine("Historical canvas A/B diagnostic: mote --canvas-experimental [path]");
            Console.WriteLine("Windows UIA fragment diagnostic: mote --canvas-experimental --uia-fragment-experimental [path]");
            Console.WriteLine("macOS diagnostic: mote --check-native-mac-workflow <input> <output>");
            Console.WriteLine("Canvas diagnostics: --check-native-windows-canvas | --check-native-mac-canvas");
            Console.WriteLine("Experimental AppKit clipboard diagnostic: --check-native-mac-canvas-clipboard <input> <output>");
            Console.WriteLine("Experimental AppKit global-delete diagnostic: --check-native-mac-canvas-delete <input>");
            Console.WriteLine("Experimental AppKit AX tree diagnostic: --check-native-mac-canvas-ax <input>");
            Console.WriteLine("Experimental AppKit horizontal diagnostic: --check-native-mac-horizontal <short-line> <long-line> <output-dir>");
            Console.WriteLine("Experimental AppKit live-theme diagnostic: --check-native-mac-theme <input.md> <output-dir> <default|canvas>");
            Console.WriteLine("Experimental AppKit marked-text commit/cancel/theme diagnostic: --check-native-mac-composition-theme <input.md> <output-dir> <default|canvas>");
            Console.WriteLine("On-screen read-only canvas: --check-native-canvas-window <100MiB-many-line-file> <50MiB-one-line-file> <output-dir> <theme-id>");
            return 0;
        }
        if (!NativeLaunchParser.TryParse(args, out var launch, out var argumentError))
        {
            Console.Error.WriteLine(argumentError);
            return 2;
        }
        if (launch is NativeLaunchRoute.CanvasDiagnostic { FragmentRoot: true } &&
            !OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("The UIA fragment diagnostic requires Windows canvas mode.");
            return 2;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("mote's native desktop shell requires Windows or macOS.");
            return 3;
        }

        var config = ValidateThemeId(MoteConfigLoader.Load(), out var themeWarning);
        var traceRequested = config.TraceEnabled || Environment.GetEnvironmentVariable("MOTE_TRACE") == "1";
        if (traceRequested)
        {
            try
            {
                MoteTelemetry.Configure(new TelemetryOptions
                {
                    Enabled = true,
                    OutputDirectory = config.TraceDirectory
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Console.Error.WriteLine("Local tracing unavailable; editing remains available.");
            }
        }

        var selectedLaunch = launch!;
        var startupMark = MoteTelemetry.Mark();
        INativeEditorShell shell = NativeShellFactory.Create(selectedLaunch);
        var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
        using var app = new NativeEditorController(shell, config, theme,
            selectedLaunch.Path,
            selectedLaunch is NativeLaunchRoute.Product product ? product.Profile : null,
            startupMark: startupMark);
        if (!selectedLaunch.Smoke && themeWarning is not null)
        {
            // The status bar counts warnings but cannot display their details.
            // A typo is rare and actionable, so show its text once after the
            // native window exists instead of silently changing appearance.
            var warningShown = false;
            var warningText = themeWarning.Message;
            shell.Shown += () =>
            {
                if (warningShown) return;
                warningShown = true;
                shell.Post(() => shell.ShowError(warningText));
            };
        }
        if (selectedLaunch.Smoke)
        {
            shell.Shown += () =>
            {
                Console.WriteLine("mote-native-gui-ready");
                shell.Post(shell.Close);
            };
        }
        app.Run();
        app.Dispose(); // Close pending intervals before draining the process-local writer.
        MoteTelemetry.ShutdownAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>
    /// Adds a nonfatal diagnostic for a syntactically valid but unregistered theme ID.
    /// The ID remains intact for display, while the policy resolver safely follows
    /// the operating system's light/dark preference. Known IDs allocate nothing.
    /// </summary>
    internal static MoteConfiguration ValidateThemeId(
        MoteConfiguration configuration, out ConfigDiagnostic? warning)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        warning = null;
        if (ThemePolicies.IsKnownId(configuration.ThemeId)) return configuration;

        warning = new ConfigDiagnostic("CONFIG_THEME",
            $"Unknown theme '{configuration.ThemeId}'. Using the system light/dark appearance. " +
            $"Edit {configuration.ConfigPath} and choose 'mote-dark', 'mote-light', " +
            "'mote-high-contrast-dark', or 'system'.");
        var diagnostics = new List<ConfigDiagnostic>(configuration.Diagnostics) { warning };
        return configuration with { Diagnostics = diagnostics.AsReadOnly() };
    }

    /// <summary>Runs the optional Windows geometry diagnostic without affecting normal editing.</summary>
    private static int CheckWindowsCanvas()
    {
        if (!OperatingSystem.IsWindows()) return 3;
        try
        {
            var result = Windows.Canvas.WindowsCanvasProbe.Run();
            Console.WriteLine($"mote-native-windows-canvas-ready cases={result.Cases} " +
                $"ascii={result.ExactAsciiHits} clusters={result.ClusterRoundTrips}");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"Windows canvas diagnostic unavailable ({ex.GetType().Name}).");
            return 4;
        }
    }

    /// <summary>Runs the optional macOS geometry/bitmap diagnostic without taking input ownership.</summary>
    private static int CheckMacCanvas()
    {
        if (!OperatingSystem.IsMacOS()) return 3;
        try
        {
            var result = Mac.Canvas.MacCanvasProbe.Run();
            Console.WriteLine($"mote-native-mac-canvas-ready cases={result.Cases.Count} " +
                $"painted={result.Cases.Sum(static item => item.PaintedBytes)}");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"macOS canvas diagnostic unavailable ({ex.GetType().Name}).");
            return 4;
        }
    }

    /// <summary>
    /// Drives an opt-in visible native canvas without replacing the working
    /// editor or accepting text input. Captures stay in the caller's repo cache.
    /// </summary>
    private static int CheckCanvasWindow(string many, string longLine, string output,
        string themeId)
    {
        var theme = ThemePolicies.All.FirstOrDefault(policy =>
            string.Equals(policy.Id, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme is null || !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return 3;
        try
        {
            var result = OperatingSystem.IsWindows()
                ? Windows.Canvas.WindowsOnScreenCanvasProbe.Run(many, longLine, output, theme)
                : Mac.Canvas.MacOnScreenCanvasProbe.Run(many, longLine, output, theme);
            var platform = OperatingSystem.IsWindows() ? "windows" : "macos";
            Console.WriteLine($"mote-native-canvas-window-ready platform={platform} " +
                $"theme={result.ThemeId} before={result.ManyLineBefore} " +
                $"after={result.ManyLineAfter} selection={result.SelectionStart}+" +
                $"{result.SelectionLength} long-slice={result.LongLineMaxSliceLength} " +
                $"hits={result.HitTestRoundTrips} screenshots={result.ScreenshotPaths.Count}");
            return 0;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Console.Error.WriteLine($"On-screen canvas diagnostic unavailable ({ex.GetType().Name}).");
            return 4;
        }
    }
}
