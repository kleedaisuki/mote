using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mote.Engine;
using Mote.Formats;

namespace Mote.MarkdownBenchmarks;

/// <summary>
/// Measures the truthful large-Markdown session contract in one Native AOT process.
/// Fixture creation is excluded; every result retains its exact completeness claim.
/// </summary>
internal static class Program
{
    private const int MiB = 1024 * 1024;
    private const int VisibleLength = 4096;

    /// <summary>Runs exactly one isolated 100 MiB workload and writes repository-local JSONL.</summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not
            ("markdown-flat" or "markdown-dense" or "markdown-complex" or "markdown-cancel"))
        {
            Console.Error.WriteLine("Usage: Mote.MarkdownBenchmarks <markdown-flat|markdown-dense|markdown-complex|markdown-cancel>");
            return 2;
        }
        var root = FindRepositoryRoot();
        var allowed = Path.GetFullPath(Path.Combine(root, ".temp", "benchmarks")) + Path.DirectorySeparatorChar;
        var scratch = Path.GetFullPath(Path.Combine(allowed, "markdown-" + Guid.NewGuid().ToString("N")));
        if (!scratch.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Markdown benchmark scratch escaped the repository.");
        var results = Path.Combine(root, ".cache", "benchmarks");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(results);
        try { return await RunAsync(scratch, results, 100, args[0]); }
        finally
        {
            if (!scratch.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing Markdown benchmark cleanup outside the repository.");
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>Finds the containing checkout instead of writing into a global temp directory.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "mote.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot locate mote.sln.");
    }

    /// <summary>Runs one sparse, dense, complex, or cancellation workload.</summary>
    private static async Task<int> RunAsync(string scratch, string resultDirectory, int sizeMiB, string mode)
    {
        var path = Path.Combine(scratch, "large-markdown.md");
        await WriteFixtureAsync(path, (long)sizeMiB * MiB, mode);
        var bytes = new FileInfo(path).Length;
        var watch = Stopwatch.StartNew();
        using var document = await Document.OpenAsync(path);
        watch.Stop();
        var openMs = watch.Elapsed.TotalMilliseconds;
        var snapshot = document.Snapshot;
        using var session = ((IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Markdown)).CreateSession();
        using var process = Process.GetCurrentProcess();
        var request = new AnalysisRequest(new TextSpan(0, Math.Min(VisibleLength, snapshot.Length)), AnalysisScope.Full);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        process.Refresh();
        var afterOpenBytes = PositiveOrNull(process.WorkingSet64);

        double initialMs;
        long initialAllocation;
        string? initialCompleteness = null;
        int? initialCoverage = null;
        int? initialDiagnosticCount = null;
        double? applyMs = null;
        double? editAnalyzeMs = null;
        long? editAllocation = null;
        string? editCompleteness = null;
        double? cancelRequestedAtMs = null;
        double? cancelObservedAtMs = null;
        double? cancelReactionMs = null;
        bool? cancelled = null;
        double? recoveryMs = null;
        long? recoveryAllocation = null;
        string? recoveryCompleteness = null;
        long? afterAnalysisBytes;
        long? afterEditBytes = null;
        long? afterRecoveryBytes = null;
        var valid = true;

        if (mode == "markdown-cancel")
        {
            using var cts = new CancellationTokenSource();
            long requestedTick = 0;
            using var timer = new Timer(_ =>
            {
                Interlocked.Exchange(ref requestedTick, Stopwatch.GetTimestamp());
                cts.Cancel();
            });
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            timer.Change(5, Timeout.Infinite);
            try
            {
                session.Analyze(snapshot, [], request, cts.Token);
                cancelled = false;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                cancelled = true;
            }
            var observed = Stopwatch.GetTimestamp();
            timer.Change(Timeout.Infinite, Timeout.Infinite);
            initialMs = Stopwatch.GetElapsedTime(start, observed).TotalMilliseconds;
            initialAllocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var requested = Interlocked.Read(ref requestedTick);
            if (requested != 0)
            {
                cancelRequestedAtMs = Stopwatch.GetElapsedTime(start, requested).TotalMilliseconds;
                cancelObservedAtMs = initialMs;
                cancelReactionMs = Stopwatch.GetElapsedTime(requested, observed).TotalMilliseconds;
            }
            process.Refresh();
            afterAnalysisBytes = PositiveOrNull(process.WorkingSet64);
            valid = cancelled == true && requested != 0;
            // A cancelled certification must not poison the per-document session.
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var recovered = session.Analyze(snapshot, [], request);
            watch.Stop();
            recoveryMs = watch.Elapsed.TotalMilliseconds;
            recoveryAllocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            recoveryCompleteness = recovered.Completeness.ToString();
            valid &= IsComplete(recovered, snapshot);
            process.Refresh();
            afterRecoveryBytes = PositiveOrNull(process.WorkingSet64);
        }
        else
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var first = session.Analyze(snapshot, [], request);
            watch.Stop();
            initialMs = watch.Elapsed.TotalMilliseconds;
            initialAllocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            initialCompleteness = first.Completeness.ToString();
            initialCoverage = first.Coverage.Length;
            initialDiagnosticCount = first.TotalDiagnosticCount;
            process.Refresh();
            afterAnalysisBytes = PositiveOrNull(process.WorkingSet64);
            valid = mode == "markdown-flat" ? IsComplete(first, snapshot) : IsProvisional(first);

            var change = new TextChange(Math.Min(64, snapshot.Length - 1), 1, "b");
            watch.Restart();
            document.Apply(change);
            watch.Stop();
            applyMs = watch.Elapsed.TotalMilliseconds;
            var edited = document.Snapshot;
            allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            watch.Restart();
            var next = session.Analyze(edited,
                [new VersionedEdit(snapshot.Version, edited.Version, change)], request);
            watch.Stop();
            editAnalyzeMs = watch.Elapsed.TotalMilliseconds;
            editAllocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            editCompleteness = next.Completeness.ToString();
            valid &= mode == "markdown-flat" ? IsComplete(next, edited) : IsProvisional(next);
            process.Refresh();
            afterEditBytes = PositiveOrNull(process.WorkingSet64);
        }

        process.Refresh();
        var result = new MarkdownLargeResult(DateTimeOffset.UtcNow, 1, mode, sizeMiB, bytes,
            RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.Version.ToString(), Environment.ProcessorCount, openMs,
            initialMs, initialAllocation, initialCompleteness, initialCoverage, initialDiagnosticCount,
            applyMs, editAnalyzeMs, editAllocation, editCompleteness,
            cancelRequestedAtMs, cancelObservedAtMs, cancelReactionMs, cancelled,
            recoveryMs, recoveryAllocation, recoveryCompleteness, afterOpenBytes, afterAnalysisBytes,
            afterEditBytes, afterRecoveryBytes, PositiveOrNull(process.PeakWorkingSet64), valid);
        var json = JsonSerializer.Serialize(result, MarkdownBenchmarkJsonContext.Default.MarkdownLargeResult);
        await File.AppendAllTextAsync(Path.Combine(resultDirectory, "markdown-large.jsonl"), json + "\n");
        Console.WriteLine(json);
        if (valid) return 0;
        Console.Error.WriteLine("Large Markdown completeness/cancellation contract failed.");
        return 1;
    }

    /// <summary>Complete means global coverage and an exact zero diagnostic count.</summary>
    private static bool IsComplete(DocumentAnalysis result, TextSnapshot snapshot) =>
        result.Completeness == AnalysisCompleteness.Complete &&
        result.Coverage.Start == 0 && result.Coverage.Length == snapshot.Length &&
        result.TotalDiagnosticCount == 0;

    /// <summary>Rejected huge inputs must not invent a whole-file diagnostic count.</summary>
    private static bool IsProvisional(DocumentAnalysis result) =>
        result.Completeness == AnalysisCompleteness.Provisional &&
        result.TotalDiagnosticCount is null;

    /// <summary>Zero from a platform runtime is unavailable memory, never zero RAM.</summary>
    private static long? PositiveOrNull(long value) => value > 0 ? value : null;

    /// <summary>Streams exact-size ASCII inputs without holding a giant fixture string.</summary>
    private static async Task WriteFixtureAsync(string path, long bytes, string mode)
    {
        var pattern = mode switch
        {
            "markdown-flat" or "markdown-cancel" =>
                Encoding.ASCII.GetBytes("alpha" + new string('a', 8185) + "\n\n"),
            "markdown-dense" => "alpha\n\n"u8.ToArray(),
            "markdown-complex" =>
                "# Heading **emphasis** [link](https://example.org)\n- item\n```json\n{\"a\":1}\n```\n\n"u8.ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
        var buffer = new byte[64 * 1024];
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, buffer.Length, FileOptions.SequentialScan);
        for (long written = 0; written < bytes;)
        {
            var count = (int)Math.Min(buffer.Length, bytes - written);
            for (var i = 0; i < count; i++) buffer[i] = pattern[(written + i) % pattern.Length];
            await stream.WriteAsync(buffer.AsMemory(0, count));
            written += count;
        }
        await stream.FlushAsync();
    }
}

/// <summary>Versioned process-isolated Native AOT evidence for large Markdown semantics.</summary>
/// <remarks>Null memory counters are unavailable; RSS includes document and analyzer.</remarks>
internal sealed record MarkdownLargeResult(
    DateTimeOffset TimestampUtc, int SchemaVersion, string Mode, int RequestedMiB, long SourceBytes,
    string Os, string Architecture, string DotNetVersion, int LogicalProcessors, double OpenMs,
    double InitialAnalyzeMs, long InitialAllocatedBytes, string? InitialCompleteness,
    int? InitialCoverageLength, int? InitialDiagnosticCount, double? ApplyMs,
    double? EditAnalyzeMs, long? EditAllocatedBytes, string? EditCompleteness,
    double? CancelRequestedAtMs, double? CancelObservedAtMs, double? CancelReactionMs,
    bool? Cancelled, double? RecoveryAnalyzeMs, long? RecoveryAllocatedBytes,
    string? RecoveryCompleteness,
    long? WorkingSetAfterOpenBytes, long? WorkingSetAfterAnalysisBytes,
    long? WorkingSetAfterEditBytes, long? WorkingSetAfterRecoveryBytes,
    long? PeakWorkingSetBytes, bool ContractPassed);

/// <summary>Source-generated metadata keeps the standalone benchmark Native AOT-safe.</summary>
[JsonSerializable(typeof(MarkdownLargeResult))]
internal partial class MarkdownBenchmarkJsonContext : JsonSerializerContext;
