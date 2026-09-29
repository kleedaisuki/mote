using System.Diagnostics;
using System.Text;
using Mote.Engine;
using Mote.Formats;

/// <summary>Cold CSV open, visible, full, and near-top edit timing probe.</summary>
/// <remarks>
/// Run from the repository root with a target MiB count and optional "utf16" or
/// "giant-row" argument.
/// The generated fixture stays under root .temp and has one ragged record per pattern.
/// This is a diagnostic probe, not a latency gate or a GUI paint measurement.
/// </remarks>
internal static class Program
{
    /// <summary>Generates a mixed CSV file and measures each distinct processing phase.</summary>
    private static async Task Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var mib) || mib <= 0 ||
            args.Length == 2 && args[1] is not ("utf16" or "giant-row"))
            throw new ArgumentException("Usage: CsvColdBenchmark <MiB> [utf16|giant-row]");

        var utf16 = args.Length == 2 && args[1] == "utf16";
        var giantRow = args.Length == 2 && args[1] == "giant-row";
        var encoding = utf16 ? Encoding.Unicode : new UTF8Encoding(false);
        var directory = Path.Combine(".temp", "CsvColdBenchmark");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "case.csv");
        var pattern = giantRow ? new string(',', 8192) :
            "key," + new string('x', 500) + "\r\n\"multi\r\nline\",2\r\nshort\r\n😀,3\r\n";
        var patternBytes = encoding.GetBytes(pattern);
        var count = 0;
        await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
        {
            await output.WriteAsync(encoding.GetPreamble());
            if (!giantRow) await output.WriteAsync(encoding.GetBytes("name,value\r\n"));
            while (output.Length < (long)mib * 1024 * 1024)
            {
                await output.WriteAsync(patternBytes);
                if (!giantRow) count++;
            }
        }

        var fileBytes = new FileInfo(path).Length;
        var stopwatch = Stopwatch.StartNew();
        using var document = await Document.OpenAsync(path);
        var openMs = stopwatch.Elapsed.TotalMilliseconds;
        var heapOpen = ManagedMiB();
        var rssOpen = WorkingSetMiB();
        using var session = new CsvPolicy().CreateSession();
        var request = new AnalysisRequest(new TextSpan(0, 256), AnalysisScope.Visible);

        stopwatch.Restart();
        var visible = session.Analyze(document.Snapshot, [], request);
        var visibleMs = stopwatch.Elapsed.TotalMilliseconds;
        var heapVisible = ManagedMiB();
        var rssVisible = WorkingSetMiB();

        stopwatch.Restart();
        var full = session.Analyze(document.Snapshot, [], request with { Scope = AnalysisScope.Full });
        var fullMs = stopwatch.Elapsed.TotalMilliseconds;
        var heapFull = ManagedMiB();
        var rssFull = WorkingSetMiB();

        var before = document.Snapshot;
        var change = new TextChange(5, 1, "X");
        var after = document.Apply(change);
        stopwatch.Restart();
        var edited = session.Analyze(after,
            [new VersionedEdit(before.Version, after.Version, change)], request);
        var editMs = stopwatch.Elapsed.TotalMilliseconds;
        var heapEdit = ManagedMiB();
        var rssEdit = WorkingSetMiB();

        Console.WriteLine($"encoding={(utf16 ? "UTF16LE" : "UTF8")},fixture={(giantRow ? "giant-row" : "mixed")},targetMiB={mib}," +
            $"fileBytes={fileBytes},patterns={count},chars={after.Length}");
        Console.WriteLine($"openMs={openMs:F1},visibleMs={visibleMs:F2}," +
            $"fullResumeMs={fullMs:F1},editMs={editMs:F2}");
        Console.WriteLine($"heapOpenMiB={heapOpen:F1},heapVisibleMiB={heapVisible:F1}," +
            $"heapFullMiB={heapFull:F1},heapEditMiB={heapEdit:F1}");
        Console.WriteLine($"rssOpenMiB={rssOpen:F1},rssVisibleMiB={rssVisible:F1}," +
            $"rssFullMiB={rssFull:F1},rssEditMiB={rssEdit:F1}");
        Console.WriteLine($"visible={visible.Completeness},coverage={visible.Coverage.Length}," +
            $"full={full.Completeness},count={full.TotalDiagnosticCount},expected={count}," +
            $"edited={edited.Completeness}");
        if (full.TotalDiagnosticCount != count)
            throw new InvalidOperationException("The global ragged-row diagnostic count changed.");
    }

    /// <summary>Forces collection to report retained managed objects, not transient allocations.</summary>
    private static double ManagedMiB() => GC.GetTotalMemory(true) / 1048576.0;

    /// <summary>Samples current process resident memory after refreshing OS counters.</summary>
    private static double WorkingSetMiB()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64 / 1048576.0;
    }
}
