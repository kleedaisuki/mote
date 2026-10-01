using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Benchmarks;

/// <summary>Measures representative open/edit/analyze operations without a CI timing gate.</summary>
internal static class Program
{
    /// <summary>Runs one process-isolated synthetic workload and writes a versioned JSONL result.</summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var sizeMiB) || sizeMiB is < 1 or > 512 ||
            (args.Length == 2 && args[1] is not ("--open-edit" or "--long-line" or "--json-key-index")))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tests/Mote.Benchmarks -c Release -- <size-MiB: 1..512> [--open-edit|--long-line|--json-key-index]");
            return 2;
        }
        var mode = args.Length == 1 ? "markdown-analysis" : args[1] switch
        {
            "--long-line" => "long-line-open-edit",
            "--json-key-index" => "json-key-index",
            _ => "open-edit"
        };
        var root = FindRepositoryRoot();
        var scratch = Path.Combine(root, ".temp", "benchmarks", Guid.NewGuid().ToString("N"));
        var resultDirectory = Path.Combine(root, ".cache", "benchmarks");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(resultDirectory);
        try
        {
            if (mode == "json-key-index")
                return await RunJsonKeyIndexAsync(scratch, resultDirectory, sizeMiB);
            var path = Path.Combine(scratch, "workload.md");
            await CreateWorkloadAsync(path, sizeMiB, mode == "long-line-open-edit");
            var stopwatch = Stopwatch.StartNew();
            using var document = await Document.OpenAsync(path);
            stopwatch.Stop();
            var openMs = stopwatch.Elapsed.TotalMilliseconds;

            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var originalLength = document.Snapshot.Length;
            var workingSetAfterOpen = process.WorkingSet64;
            var snapshot = document.Snapshot;
            stopwatch.Restart();
            document.Apply(new TextChange(snapshot.Length - 1, 0, "edited "));
            stopwatch.Stop();
            var editEndMs = stopwatch.Elapsed.TotalMilliseconds;

            snapshot = document.Snapshot;
            stopwatch.Restart();
            document.Apply(new TextChange(Math.Min(64, snapshot.Length), 0, "edited "));
            stopwatch.Stop();
            var editStartMs = stopwatch.Elapsed.TotalMilliseconds;

            snapshot = document.Snapshot;
            stopwatch.Restart();
            document.Apply(new TextChange(snapshot.Length / 2, 0, "edited "));
            stopwatch.Stop();
            var editMiddleMs = stopwatch.Elapsed.TotalMilliseconds;
            process.Refresh();
            var workingSetAfterEdits = process.WorkingSet64;
            var privateBytesAfterEdits = process.PrivateMemorySize64;

            double? analyzeMs = null;
            int? topLevelNodes = null;
            int? diagnosticCount = null;
            if (mode == "markdown-analysis")
            {
                stopwatch.Restart();
                var analysis = DocumentPolicies.ForKind(DocumentKind.Markdown).Analyze(document.Snapshot.GetText());
                stopwatch.Stop();
                analyzeMs = stopwatch.Elapsed.TotalMilliseconds;
                topLevelNodes = analysis.Root.Children.Count;
                diagnosticCount = analysis.Diagnostics.Count;
            }
            process.Refresh();
            var peakWorkingSet = process.PeakWorkingSet64;
            long? observedPeak = peakWorkingSet > 0 ? peakWorkingSet : null;
            long? observedPrivate = privateBytesAfterEdits > 0 ? privateBytesAfterEdits : null;
            var result = new BenchmarkResult(
                DateTimeOffset.UtcNow, mode, sizeMiB, document.Snapshot.Length, RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(),
                Environment.ProcessorCount, openMs, editEndMs, analyzeMs, observedPeak,
                topLevelNodes, diagnosticCount, 3, originalLength, editStartMs, editMiddleMs,
                editEndMs, workingSetAfterOpen, workingSetAfterEdits,
                observedPrivate);
            var json = JsonSerializer.Serialize(result, BenchmarkJsonContext.Default.BenchmarkResult);
            await File.AppendAllTextAsync(Path.Combine(resultDirectory, "results.jsonl"), json + "\n");
            Console.WriteLine(json);
            return 0;
        }
        finally
        {
            var allowed = Path.GetFullPath(Path.Combine(root, ".temp", "benchmarks")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(scratch).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Benchmark cleanup escaped repository .temp/benchmarks.");
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// Generates a high-cardinality JSON object before measuring a one-thread semantic pass.
    /// Source creation and file open are excluded from the allocation guard; elapsed and RSS
    /// remain non-gating because hosted runners vary in scheduling and physical memory.
    /// </summary>
    private static async Task<int> RunJsonKeyIndexAsync(string scratch, string resultDirectory, int sizeMiB)
    {
        var path = Path.Combine(scratch, "unique-keys.json");
        var keys = await CreateUniqueJsonAsync(path, sizeMiB);
        var stopwatch = Stopwatch.StartNew();
        using var document = await Document.OpenAsync(path);
        stopwatch.Stop();
        var openMs = stopwatch.Elapsed.TotalMilliseconds;
        var policy = (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json);
        using var process = Process.GetCurrentProcess();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var beforeAllocation = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Restart();
        using var session = policy.CreateSession();
        var snapshot = document.Snapshot;
        var analysis = session.Analyze(snapshot, [],
            new AnalysisRequest(new TextSpan(0, Math.Min(64, snapshot.Length)), AnalysisScope.Full));
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocation;
        process.Refresh();
        var result = new JsonKeyIndexResult(DateTimeOffset.UtcNow, 1, "json-key-index", sizeMiB,
            new FileInfo(path).Length, keys, snapshot.Length, openMs, stopwatch.Elapsed.TotalMilliseconds,
            allocated, process.PeakWorkingSet64, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(),
            analysis.Completeness.ToString(), analysis.TotalDiagnosticCount);
        var json = JsonSerializer.Serialize(result, BenchmarkJsonContext.Default.JsonKeyIndexResult);
        await File.AppendAllTextAsync(Path.Combine(resultDirectory, "json-key-index.jsonl"), json + "\n");
        Console.WriteLine(json);
        // 512 MiB is a coarse allocation regression guard: comfortably above the
        // measured ~205 MiB optimized 100 MiB pass, but below the old ~1.4 GiB pass.
        // It is not an RSS or latency threshold and should be revised only with evidence.
        const long allocationLimit = 512L * 1024 * 1024;
        if (analysis.Completeness == AnalysisCompleteness.Complete &&
            analysis.TotalDiagnosticCount == 0 && analysis.Diagnostics.Count == 0 &&
            allocated < allocationLimit) return 0;
        Console.Error.WriteLine("JSON unique-key semantics or analyzer allocation guard failed.");
        return 1;
    }

    /// <summary>Writes approximately the target byte size with distinct, short ASCII keys.</summary>
    private static async Task<int> CreateUniqueJsonAsync(string path, int sizeMiB)
    {
        var targetBytes = (long)sizeMiB * 1024 * 1024;
        var keys = 0;
        long written = 1;
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false), 128 * 1024))
        {
            writer.Write('{');
            while (written + 15 < targetBytes)
            {
                if (keys != 0) writer.Write(',');
                writer.Write("\"k");
                writer.Write(keys.ToString("D8"));
                writer.Write("\":0");
                written += keys == 0 ? 13 : 14;
                keys++;
            }
            writer.Write('}');
            await writer.FlushAsync();
        }
        return keys;
    }

    /// <summary>Locates the checkout so all benchmark artifacts remain project-local.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mote.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate mote.sln.");
    }

    /// <summary>Creates a deterministic line-oriented file with approximately the requested byte size.</summary>
    private static async Task CreateWorkloadAsync(string path, int sizeMiB, bool longLine)
    {
        const string line = "# Heading\nA short paragraph with **emphasis** and [link](https://example.com).\n\n";
        var targetBytes = (long)sizeMiB * 1024 * 1024;
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024);
        var pattern = Encoding.UTF8.GetBytes(longLine ? "a" : line);
        var batch = new byte[64 * 1024];
        for (var i = 0; i < batch.Length; i++) batch[i] = pattern[i % pattern.Length];
        while (stream.Length < targetBytes)
            await stream.WriteAsync(batch.AsMemory(0, (int)Math.Min(batch.Length, targetBytes - stream.Length)));
        await stream.FlushAsync();
    }
}

/// <summary>Versioned process-level open/edit result; original fields remain for prior readers.</summary>
/// <remarks>Peak and private bytes are null when the platform runtime reports zero/unavailable.</remarks>
internal sealed record BenchmarkResult(
    DateTimeOffset TimestampUtc, string Mode, int RequestedMiB, int TextLengthUtf16, string Os,
    string Architecture, string DotNetVersion, int LogicalProcessors,
    double OpenMs, double EditMs, double? AnalyzeMs, long? PeakWorkingSetBytes,
    int? TopLevelNodes, int? DiagnosticCount, int SchemaVersion, int OriginalLengthUtf16,
    double EditStartMs, double EditMiddleMs, double EditEndMs,
    long WorkingSetAfterOpenBytes, long WorkingSetAfterEditsBytes, long? PrivateBytesAfterEdits);

/// <summary>Static serializer metadata so the same benchmark also runs as Native AOT.</summary>
[JsonSerializable(typeof(BenchmarkResult))]
[JsonSerializable(typeof(JsonKeyIndexResult))]
internal partial class BenchmarkJsonContext : JsonSerializerContext;

/// <summary>Process-isolated JSON high-cardinality result; kept separate from legacy JSONL schema.</summary>
internal sealed record JsonKeyIndexResult(
    DateTimeOffset TimestampUtc, int SchemaVersion, string Mode, int RequestedMiB,
    long SourceBytes, int UniqueKeys, int TextLengthUtf16,
    double OpenMs, double AnalyzeMs, long AnalyzerAllocatedBytes, long PeakWorkingSetBytes,
    string Os, string Architecture, string DotNetVersion,
    string Completeness, int? TotalDiagnosticCount);
