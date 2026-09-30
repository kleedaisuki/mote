using System.Runtime.Versioning;
using System.Text.Json;
using Mote.Engine;
using Mote.Native.Viewport;
using Mote.Telemetry;
using Mote.Themes;

namespace Mote.Native.Mac;

/// <summary>Opt-in target audit of actual source draw callbacks, not screen presentation.</summary>
/// <remarks>Owns synthetic unsaved text and local traces; never loads user configuration or sends input.</remarks>
[SupportedOSPlatform("macos")]
internal static class MacDrawTraceProbe
{
    /// <summary>Runs exactly one AppKit profile from a repository root, with a hard 20-second deadline.</summary>
    internal static int Run(bool continuous)
    {
        // A blocked native call cannot execute a posted close. The watchdog is failure-only;
        // normal completion closes AppKit and drains telemetry before checking evidence.
        using var watchdog = new Timer(static _ => Environment.Exit(124), null,
            TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);
        try
        {
            var directory = CreateTraceDirectory();
            MoteTelemetry.Configure(new TelemetryOptions
            {
                Enabled = true, OutputDirectory = directory, QueueCapacity = 64,
                MaxFilesPerSession = 1, MaxFileBytes = 64 * 1024
            });
            Exception? failure = null;
            try
            {
                var shell = new MacEditorShell(experimentalCanvas: continuous);
                shell.Shown += () => PostAfter(shell, () =>
                {
                    try
                    {
                        CheckDraw(shell, continuous);
                        // displayIfNeeded is not a promise of immediate drawRect on every
                        // AppKit view. Keep the matching revision alive for normal drawing.
                        PostAfter(shell, () => { shell.CancelSourceDrawTrace(); shell.Close(); });
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        failure = error;
                        shell.CancelSourceDrawTrace();
                        shell.Close();
                    }
                });
                shell.Run();
                if (failure is not null) throw failure;
                Require(!MoteTelemetry.Health.SinkFaulted && MoteTelemetry.Health.DroppedRecords == 0,
                    "healthy bounded trace sink");
            }
            finally { MoteTelemetry.ShutdownAsync().GetAwaiter().GetResult(); }
            var drainedHealth = MoteTelemetry.Health;
            Require(!drainedHealth.Enabled && !drainedHealth.SinkFaulted && drainedHealth.DroppedRecords == 0,
                "telemetry detached after drain");
            CheckRecords(directory);
            Console.WriteLine($"mote-native-mac-draw-trace-ready mode={(continuous ? "continuous" : "legacy")}; endpoint=source-draw-return; physical-presentation=not-tested");
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var identifier = error is ProbeContractFailure contract ? contract.Identifier : error.GetType().Name;
            Console.Error.WriteLine($"Mac draw trace check failed: {identifier}.");
            return 1;
        }
    }

    /// <summary>Installs a wrong-version source first, then accepts and installs the matching mutation.</summary>
    private static void CheckDraw(MacEditorShell shell, bool continuous)
    {
        ObjC.Send(ObjC.Send(shell.ProbeWindow, ObjC.Sel("contentView")), ObjC.Sel("layoutSubtreeIfNeeded"));
        using var document = new Document("synthetic source\nsecond line\n");
        shell.SetTheme(ThemePolicies.Resolve(ThemePolicies.DarkId, true));
        Install(shell, document.Snapshot, continuous);
        var cancelledParent = MoteTelemetry.Mark();
        shell.TraceSourceDraw(new(17, 0), MoteTelemetry.Fork(cancelledParent), new(Version: 0));
        var parent = MoteTelemetry.Mark();
        document.Apply(new TextChange(0, 0, "accepted "));
        Require(document.Snapshot.Version == 1, "canonical accepted version");
        shell.TraceSourceDraw(new(17, 1), MoteTelemetry.Fork(parent), new(Version: 1));
        Draw(shell, continuous); // Installed version 0 cannot complete pending version 1.
        Install(shell, document.Snapshot, continuous);
        Draw(shell, continuous);
        Draw(shell, continuous); // Duplicate native callbacks must not duplicate the interval.
        MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, cancelledParent,
            new(Version: 0), TelemetryStatus.Cancelled);
        MoteTelemetry.RecordElapsed(TelemetryOperation.EditToPresentation, parent, new(Version: 1));
    }

    /// <summary>Uses the production source installation contract without a controller or file path.</summary>
    private static void Install(MacEditorShell shell, TextSnapshot snapshot, bool continuous)
    {
        if (!continuous)
        {
            shell.SetDocument(new NativeDocumentView("Draw audit", snapshot.GetText(), 0,
                snapshot.Length, true, "", new(17, snapshot.Version)));
            Require(shell.ProbeDocumentStamp == new NativeDocumentStamp(17, snapshot.Version), "legacy installed stamp");
            return;
        }
        var frame = new CanvasInteraction(snapshot, 20, 400).Frame();
        // The source canvas owns the complete immutable snapshot, while its
        // native input island accepts only a bounded single-line source slice.
        var firstLine = snapshot.GetText().Split('\n', 2)[0];
        shell.SetCanvasBinding(new NativeCanvasBinding(17, snapshot.Version, snapshot.Version + 1,
            snapshot, frame, 0, firstLine, 0, 0, "Draw audit", "", true));
        shell.ProbeCanvasPublishBodyHeight();
        Require(shell.ProbeCanvasStamp == new NativeDocumentStamp(17, snapshot.Version), "canvas installed stamp");
        Require(shell.ProbeCanvasBodyRect.Size.Height > 0, "positive canvas source body");
    }

    /// <summary>Requests real AppKit drawing with its graphics context; never invokes drawRect directly.</summary>
    private static void Draw(MacEditorShell shell, bool continuous)
    {
        var surface = continuous ? shell.ProbeCanvasView : shell.ProbeEditorView;
        Require(surface != 0, "owned native source view");
        ObjC.Send(surface, ObjC.Sel("setNeedsDisplay:"), (nint)1);
        ObjC.Send(surface, ObjC.Sel("displayIfNeeded"));
        ObjC.Send(shell.ProbeWindow, ObjC.Sel("displayIfNeeded"));
    }

    /// <summary>Yields to AppKit rather than recursively consuming the shell's posted-action queue.</summary>
    private static void PostAfter(MacEditorShell shell, Action action)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(250).ConfigureAwait(false);
            shell.Post(action);
        });
    }

    /// <summary>Restricts writes to a fresh directory below a nonsymlink repository .temp ancestry.</summary>
    private static string CreateTraceDirectory()
    {
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        Require(File.Exists(Path.Combine(root, "src", "Mote.Native", "Mote.Native.csproj")) &&
            (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))), "repository working directory");
        var directory = root;
        for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
            Require((ancestor.Attributes & FileAttributes.ReparsePoint) == 0, "nonredirected repository ancestry");
        foreach (var segment in new[] { ".temp", "mac-draw-trace", Guid.NewGuid().ToString("N") })
        {
            directory = Path.Combine(directory, segment);
            Directory.CreateDirectory(directory);
            Require((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0, "nonredirected trace ancestry");
        }
        return directory;
    }

    /// <summary>Requires exact content-free causal records after the writer has been drained.</summary>
    private static void CheckRecords(string directory)
    {
        var records = Directory.GetFiles(directory, "*.jsonl").SelectMany(File.ReadLines)
            .Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
        Require(records.Length == 5, "exact terminal and session record count");
        var fields = new HashSet<string>(["schema_version", "utc_time", "session_id", "trace_id",
            "span_id", "parent_span_id", "operation", "duration_us", "status", "attributes"]);
        foreach (var record in records)
        {
            Require(record.EnumerateObject().All(field => fields.Contains(field.Name)), "content-free schema");
            Require(record.GetProperty("schema_version").GetInt32() == 1 &&
                record.GetProperty("duration_us").GetInt64() >= 0, "schema and monotonic duration");
            Require(record.GetProperty("attributes").EnumerateObject().All(field => field.Name == "version"), "numeric-only dimensions");
        }
        var session = records.Single(record => record.GetProperty("operation").GetString() == "mote.session");
        Require(session.GetProperty("status").GetString() == "success" &&
            session.GetProperty("parent_span_id").ValueKind == JsonValueKind.Null &&
            !session.GetProperty("attributes").EnumerateObject().Any(), "valid drained session record");
        var endpoints = records.Where(record => record.GetProperty("operation").GetString() != "mote.session").ToArray();
        Require(endpoints.Length == 4 && records.Select(record => record.GetProperty("span_id").GetString()).Distinct().Count() == 5,
            "unique causal spans");
        Require(endpoints.All(record => record.GetProperty("session_id").GetString() == session.GetProperty("session_id").GetString() &&
            record.GetProperty("trace_id").GetString() == session.GetProperty("trace_id").GetString() &&
            record.GetProperty("attributes").EnumerateObject().Count() == 1), "single drained session and version dimension");
        foreach (var version in new long[] { 0, 1 })
        {
            var pair = endpoints.Where(record => record.GetProperty("attributes").GetProperty("version").GetInt64() == version).ToArray();
            Require(pair.Length == 2, "one endpoint and parent per version");
            var draw = pair.Single(record => record.GetProperty("operation").GetString() == "document.edit_to_draw_submission");
            var parent = pair.Single(record => record.GetProperty("operation").GetString() == "document.edit_to_presentation");
            Require(draw.GetProperty("status").GetString() == (version == 0 ? "cancelled" : "success"), "exact draw terminal status");
            Require(parent.GetProperty("status").GetString() == (version == 0 ? "cancelled" : "success") &&
                parent.GetProperty("parent_span_id").GetString() == session.GetProperty("span_id").GetString(), "parent terminal status and session linkage");
            Require(draw.GetProperty("parent_span_id").GetString() == parent.GetProperty("span_id").GetString() &&
                draw.GetProperty("trace_id").GetString() == parent.GetProperty("trace_id").GetString(), "causal parent linkage");
        }
    }

    /// <summary>Fails closed rather than reporting callback evidence that was not observed.</summary>
    private static void Require(bool condition, string contract)
    {
        if (!condition) throw new ProbeContractFailure(contract.Replace(' ', '_'));
    }

    /// <summary>Contains only a fixed in-code assertion identifier, never native exception text.</summary>
    private sealed class ProbeContractFailure(string identifier) : Exception
    {
        /// <summary>Allowlisted by construction at the private Require call sites.</summary>
        internal string Identifier { get; } = identifier;
    }
}
