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
            Console.WriteLine("Usage: mote [path] [--smoke-gui|--check-runtime]");
            Console.WriteLine("macOS diagnostic: mote --check-native-mac-workflow <input> <output>");
            return 0;
        }
        var smoke = args.Length == 1 && args[0] == "--smoke-gui";
        if (!smoke && args.Length > 1)
        {
            Console.Error.WriteLine("mote opens one file per process; provide at most one path.");
            return 2;
        }
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("mote's native desktop shell requires Windows or macOS.");
            return 3;
        }

        var config = MoteConfigLoader.Load();
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

        INativeEditorShell shell = OperatingSystem.IsWindows()
            ? new Windows.WindowsEditorShell()
            : new Mac.MacEditorShell();
        var theme = ThemePolicies.Resolve(config.ThemeId, shell.PrefersDark);
        using var app = new NativeEditorController(shell, config, theme, smoke ? null : args.FirstOrDefault());
        if (smoke)
        {
            shell.Shown += () =>
            {
                Console.WriteLine("mote-native-gui-ready");
                shell.Post(shell.Close);
            };
        }
        app.Run();
        MoteTelemetry.ShutdownAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        return 0;
    }
}
