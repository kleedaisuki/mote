using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using Mote.Engine;
using Mote.Native.Viewport;
using Mote.Themes;

namespace Mote.Native.Mac.Canvas;

/// <summary>
/// Exercises an actual top-level AppKit NSView over two large engine documents.
/// No full NSTextView mirror, input client, or parser state is constructed.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOnScreenCanvasProbe
{
    private const int FormerPageBoundary = 64 * 1024;
    private const int MaxSliceLength = 16 * 1024;
    private const int WindowHeight = 640;

    /// <summary>
    /// Displays, scrolls, selects and captures the bounded canvas on macOS.
    /// The output directory must be below repository .cache or .temp.
    /// </summary>
    internal static CanvasWindowProbeResult Run(string manyLinePath,
        string longLinePath, string outputDirectory, IThemePolicy theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var output = ValidateOutputDirectory(outputDirectory);
        if (new FileInfo(manyLinePath).Length < 95L * 1024 * 1024 ||
            new FileInfo(longLinePath).Length < 45L * 1024 * 1024)
            throw new ArgumentException("The probe requires approximately 100 MiB and 50 MiB inputs.");

        using var many = Document.OpenAsync(manyLinePath).GetAwaiter().GetResult();
        using var longLine = Document.OpenAsync(longLinePath).GetAwaiter().GetResult();
        var lineHeight = Math.Max(18, theme.Typography.EditorFontSize *
            theme.Typography.LineHeightMultiplier);
        var manyInteraction = new CanvasInteraction(many.Snapshot, lineHeight,
            WindowHeight, MaxSliceLength);
        var longInteraction = new CanvasInteraction(longLine.Snapshot, lineHeight,
            WindowHeight, MaxSliceLength);
        var surface = new MacOnScreenCanvasSurface(manyInteraction, theme);
        var paths = new List<string>(5);
        var before = -1;
        var after = -1;
        var selectionStart = -1;
        var selectionLength = 0;
        var longMax = 0;

        surface.Run(view =>
        {
            var snapshot = manyInteraction.Snapshot;
            if (snapshot.Length <= FormerPageBoundary + 8192 || snapshot.LineCount < 100)
                throw new InvalidOperationException("The many-line fixture has too few rows.");
            var seamLine = snapshot.GetLineIndexFromOffset(FormerPageBoundary);
            var startLine = Math.Max(0, seamLine - 4);
            manyInteraction.Reveal(snapshot.GetLineStartOffset(startLine));
            view.Display();
            before = manyInteraction.TopAnchor.SourceOffset;
            if (before >= FormerPageBoundary)
                throw new InvalidOperationException("The initial viewport starts beyond the seam.");
            var beforePng = Path.Combine(output, $"mac-canvas-many-before-{theme.Id}.png");
            view.CapturePng(beforePng);
            paths.Add(beforePng);
            var startSlice = manyInteraction.Frame().Slices[0];
            view.ProbeRoundTrip(startSlice.SourceStart);
            view.SimulatePointerDown(16, startSlice.TopY + startSlice.Height / 2);
            // Same path as scrollWheel:, using a precise trackpad delta.
            view.SimulateWheel(-700, precise: true);
            view.Display();
            after = manyInteraction.TopAnchor.SourceOffset;
            if (after <= FormerPageBoundary)
                throw new InvalidOperationException("Wheel scrolling did not cross the former page seam.");
            var afterPng = Path.Combine(output, $"mac-canvas-many-after-{theme.Id}.png");
            view.CapturePng(afterPng);
            paths.Add(afterPng);
            var visible = manyInteraction.Frame().Slices;
            var endSlice = visible[Math.Min(visible.Count - 1, 4)];
            view.SimulatePointerDrag(16, endSlice.TopY + endSlice.Height / 2);
            view.SimulatePointerUp();
            view.Display();
            var selected = manyInteraction.Frame();
            selectionStart = selected.SelectionStart;
            selectionLength = selected.SelectionLength;
            if (selectionStart >= FormerPageBoundary ||
                selectionStart + selectionLength <= FormerPageBoundary)
                throw new InvalidOperationException("Pointer drag did not cross the former page seam.");
            var manyPng = Path.Combine(output, $"mac-canvas-many-selected-{theme.Id}.png");
            view.CapturePng(manyPng);
            paths.Add(manyPng);

            if (longInteraction.Snapshot.LineCount != 1)
                throw new InvalidOperationException("The long-line fixture must have one logical row.");
            view.SetInteraction(longInteraction);
            longInteraction.Reveal(longInteraction.Snapshot.Length * 3 / 4);
            view.Display();
            var longFrame = longInteraction.Frame();
            longMax = longFrame.Slices.Max(static slice => slice.SourceLength);
            if (longMax <= 0 || longMax > MaxSliceLength ||
                !longFrame.Slices[0].HasHiddenPrefix || !longFrame.Slices[0].HasHiddenSuffix)
                throw new InvalidOperationException("The long-line canvas shaped an unbounded or nonremote slice.");
            view.ProbeRoundTrip(longFrame.Slices[0].SourceStart);
            var longPng = Path.Combine(output, $"mac-canvas-long-{theme.Id}.png");
            view.CapturePng(longPng);
            paths.Add(longPng);

            // Empty LF and CRLF rows must hit their own source boundaries and
            // retain visible newline selection paint, not fall back to last row.
            using var blank = new Document("first\n\nthird\r\n\r\nend");
            var blankInteraction = new CanvasInteraction(blank.Snapshot, lineHeight,
                WindowHeight, MaxSliceLength);
            view.SetInteraction(blankInteraction);
            view.Display();
            var rows = blankInteraction.Frame().Slices;
            var emptyLf = rows.Single(static row => row.Line == 1);
            var emptyCrLf = rows.Single(static row => row.Line == 3);
            if (emptyLf.SourceLength != 0 || emptyCrLf.SourceLength != 0 ||
                blank.Snapshot.GetLineStartOffset(4) - emptyCrLf.SourceStart != 2)
                throw new InvalidOperationException("The LF/CRLF blank-line fixture is invalid.");
            view.SimulatePointerDown(16, emptyLf.TopY + emptyLf.Height / 2);
            view.SimulatePointerUp();
            if (blankInteraction.Selection.Active != emptyLf.SourceStart)
                throw new InvalidOperationException("An empty LF row hit the wrong source line.");
            view.SimulatePointerDown(16, emptyCrLf.TopY + emptyCrLf.Height / 2);
            view.SimulatePointerUp();
            if (blankInteraction.Selection.Active != emptyCrLf.SourceStart)
                throw new InvalidOperationException("An empty CRLF row hit the wrong source line.");
            view.SimulatePointerDown(16, rows[0].TopY + rows[0].Height / 2);
            view.SimulatePointerDrag(16, rows[^1].TopY + rows[^1].Height / 2);
            view.SimulatePointerUp();
            var newlineSelection = blankInteraction.Frame();
            if (newlineSelection.SelectionStart > emptyLf.SourceStart ||
                newlineSelection.SelectionStart + newlineSelection.SelectionLength <=
                    emptyCrLf.SourceStart + 1)
                throw new InvalidOperationException("Pointer selection omitted an empty newline row.");
            view.Display();
            var newlinePng = Path.Combine(output, $"mac-canvas-newlines-{theme.Id}.png");
            view.CapturePng(newlinePng);
            paths.Add(newlinePng);
            if (view.ScreenDrawCount < 3)
                throw new InvalidOperationException("The visible NSView did not paint each state.");
        });

        if (surface.HitTestCount < 4 || surface.RoundTripCount < 2)
            throw new InvalidOperationException("Canvas pointer geometry was not exercised.");
        WriteMetadata(output, theme, surface, paths);
        return new CanvasWindowProbeResult(theme.Id, before, after,
            selectionStart, selectionLength, longMax, surface.RoundTripCount, paths);
    }

    private static void WriteMetadata(string output, IThemePolicy theme,
        MacOnScreenCanvasSurface surface, IReadOnlyList<string> paths)
    {
        ObjC.ApplicationLoad();
        var pool = ObjC.New("NSAutoreleasePool");
        string version;
        try
        {
            var process = ObjC.Send(ObjC.Class("NSProcessInfo"), ObjC.Sel("processInfo"));
            version = ObjC.ManagedString(ObjC.Send(process,
                ObjC.Sel("operatingSystemVersionString")));
        }
        finally { ObjC.Send(pool, ObjC.Sel("release")); }
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException(
            "The published executable path is unavailable.");
        using var stream = File.OpenRead(executable);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        var command = string.Join(' ', Environment.GetCommandLineArgs().Select(
            static value => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'"));
        var lines = new List<string>
        {
            $"command={command}",
            $"macos_version={version}",
            $"display_scale={surface.DisplayScale.ToString("R", CultureInfo.InvariantCulture)}",
            $"theme_id={theme.Id}",
            $"resolved_font={surface.ResolvedFont}",
            $"executable_sha256={hash}",
            $"visible_draw_count={surface.VisibleDrawCount}"
        };
        lines.AddRange(paths.Select(static path => $"png={path}"));
        File.WriteAllLines(Path.Combine(output, $"mac-canvas-{theme.Id}-metadata.txt"),
            lines, new UTF8Encoding(false));
    }

    private static string ValidateOutputDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var output = Path.GetFullPath(path);
        var root = Path.GetFullPath(Environment.CurrentDirectory);
        var cache = Path.Combine(root, ".cache") + Path.DirectorySeparatorChar;
        var temp = Path.Combine(root, ".temp") + Path.DirectorySeparatorChar;
        var basePath = output.StartsWith(cache, StringComparison.Ordinal)
            ? Path.Combine(root, ".cache")
            : output.StartsWith(temp, StringComparison.Ordinal)
                ? Path.Combine(root, ".temp") : null;
        if (basePath is null)
            throw new ArgumentException("Canvas captures must remain under repository .cache or .temp.", nameof(path));
        var parent = new DirectoryInfo(basePath);
        if (parent.Exists && parent.LinkTarget is not null)
            throw new IOException("The artifact root must not be a symbolic link.");
        var relative = Path.GetRelativePath(basePath, output);
        foreach (var component in relative.Split(Path.DirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries))
        {
            parent = new DirectoryInfo(Path.Combine(parent.FullName, component));
            if (parent.Exists && parent.LinkTarget is not null)
                throw new IOException("Canvas output must not traverse a symbolic link.");
        }
        Directory.CreateDirectory(output);
        return output;
    }
}
