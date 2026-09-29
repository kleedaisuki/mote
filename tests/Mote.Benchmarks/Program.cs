using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Mote.Engine;
using Mote.Formats;

namespace Mote.Benchmarks;

/// <summary>Measures representative open/edit/analyze operations without a CI timing gate.</summary>
internal static class Program
{
    /// <summary>Runs one reproducible synthetic Markdown workload and writes JSONL results.</summary>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length is < 1 or > 2 || !int.TryParse(args[0], out var sizeMiB) || sizeMiB is < 1 or > 512 ||
            (args.Length == 2 && args[1] is not ("--open-edit" or "--long-line")))
        {
            Console.Error.WriteLine("Usage: dotnet run --project tests/Mote.Benchmarks -c Release -- <size-MiB: 1..512> [--open-edit|--long-line]");
            return 2;
        }
        var mode = args.Length == 1 ? "markdown-analysis" : args[1] == "--long-line" ? "long-line-open-edit" : "open-edit";
        var root = FindRepositoryRoot();
        var scratch = Path.Combine(root, ".temp", "benchmarks", Guid.NewGuid().ToString("N"));
        var resultDirectory = Path.Combine(root, ".cache", "benchmarks");
        Directory.CreateDirectory(scratch);
        Directory.CreateDirectory(resultDirectory);
        try
        {
            var path = Path.Combine(scratch, "workload.md");
            await CreateWorkloadAsync(path, sizeMiB, mode == "long-line-open-edit");
            var stopwatch = Stopwatch.StartNew();
            using var document = await Document.OpenAsync(path);
            stopwatch.Stop();
            var openMs = stopwatch.Elapsed.TotalMilliseconds;

            var snapshot = document.Snapshot;
            stopwatch.Restart();
            document.Apply(new TextChange(snapshot.Length - 1, 0, "edited "));
            stopwatch.Stop();
            var editMs = stopwatch.Elapsed.TotalMilliseconds;

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
            using var process = Process.GetCurrentProcess();
            var result = new BenchmarkResult(
                DateTimeOffset.UtcNow, mode, sizeMiB, document.Snapshot.Length, RuntimeInformation.OSDescription,
                RuntimeInformation.ProcessArchitecture.ToString(), Environment.Version.ToString(),
                Environment.ProcessorCount, openMs, editMs, analyzeMs, process.PeakWorkingSet64,
                topLevelNodes, diagnosticCount);
            var json = JsonSerializer.Serialize(result);
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

    /// <summary>One JSONL row containing workload, environment, timings, and process memory.</summary>
    private sealed record BenchmarkResult(
        DateTimeOffset TimestampUtc, string Mode, int RequestedMiB, int TextLengthUtf16, string Os,
        string Architecture, string DotNetVersion, int LogicalProcessors,
        double OpenMs, double EditMs, double? AnalyzeMs, long PeakWorkingSetBytes,
        int? TopLevelNodes, int? DiagnosticCount);
}
