using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mote.Engine;
using Mote.Formats;

if (!File.Exists("mote.sln"))
    throw new InvalidOperationException("Run from the repository root; every fixture/output stays in .temp or .cache.");
if (args.Length == 1 && args[0] == "generate")
{
    Corpus.GenerateAll();
    return;
}
if (args.Length is < 3 or > 4 || args[0] is not ("cold" or "warm" or "engine"))
    throw new ArgumentException("Usage: generate | cold|warm|engine <corpus.json> <result.json> [edit count, default 5200]");
var mode = args[0];
var path = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var root = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
if (!path.StartsWith(root + ".temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
    !output.StartsWith(root + ".cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Fixture must be under .temp and result must be under .cache.");
var fixture = JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path + ".metadata.json"))!;
using var document = await Document.OpenAsync(path);
// Let the producer's completion callback unwind before measuring steady-state retained heap.
// Without this hop its hoisted decode chunks can remain rooted throughout this inline continuation.
await Task.Yield();
using var session = new JsonPolicy().CreateSession();
var process = Process.GetCurrentProcess();
var measurements = new List<object>();
var beforeLive = GC.GetTotalMemory(true);
DocumentAnalysis? analysis = null;
var range = new TextSpan(0, Math.Min(2048, document.Snapshot.Length));
if (mode != "engine") measurements.Add(Measure("cold-full", [], AnalysisScope.Full));
var afterFullLive = GC.GetTotalMemory(true);
process.Refresh();
var fullWorking = process.WorkingSet64;
var fullPeakWorking = process.PeakWorkingSet64;
if (mode is "warm" or "engine")
{
    var count = args.Length == 4 ? int.Parse(args[3]) : 5200;
    if (count < 1 || count > 10000) throw new ArgumentOutOfRangeException(nameof(count));
    for (var i = 0; i < count; i++)
    {
        var record = i < 200
            ? new long[] { 1, fixture.Records / 2, fixture.Records - 2 }[i % 3]
            : (long)(i - 200) * 104729 % fixture.Records;
        // A primitive token at an exact page start is a boundary edit, not the ordinary owner-local workload.
        if (fixture.Kind == "primitive" && record % 32768 == 0) record++;
        var offset = checked((int)(fixture.HeaderUnits + record * fixture.RecordStride + fixture.EditOffset));
        var before = document.Snapshot;
        var edit = new TextChange(offset, 1, fixture.Kind == "primitive" ? (i % 2 == 0 ? "1" : "0") : (i % 2 == 0 ? "X" : "Y"));
        var after = document.Apply(edit);
        range = new TextSpan(Math.Max(0, offset - 512), Math.Min(2048, after.Length - Math.Max(0, offset - 512)));
        if (mode == "warm")
            measurements.Add(Measure(i < 200 ? "local-edit" : "dispersed-edit", [new(before.Version, after.Version, edit)], AnalysisScope.Visible));
    }
}
var afterEditsLive = GC.GetTotalMemory(true);
process.Refresh();
var assemblyPath = typeof(JsonPolicy).Assembly.Location;
var result = new
{
    mode, fixture, sourceUtf16Units = document.Snapshot.Length,
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), stopwatchFrequency = Stopwatch.Frequency,
    formatsAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))),
    benchmarkAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),
    engineAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Document).Assembly.Location))),
    liveBefore = beforeLive, afterFullLive, fullLiveDelta = afterFullLive - beforeLive,
    afterEditsLive, editsLiveDelta = afterEditsLive - afterFullLive,
    fullWorking, fullPeakWorking, finalWorking = process.WorkingSet64, finalPeakWorking = process.PeakWorkingSet64,
    certificateLayout = CertificateLayout.Measure(session),
    engineGraph = EngineGraph.Inspect(document),
    measurements
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{mode} {fixture.Kind}/{fixture.Newline}/{fixture.MiB} MiB -> {output}");
GC.KeepAlive(analysis);

/// <summary>Times only Analyze; source edits, reflection, result recording, and forced GC are excluded.</summary>
object Measure(string phase, IReadOnlyList<VersionedEdit> edits, AnalysisScope scope)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var allAllocated = GC.GetTotalAllocatedBytes(false);
    var started = Stopwatch.GetTimestamp();
    analysis = session.Analyze(document.Snapshot, edits, new(range, scope));
    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    var allocation = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var allocationAll = GC.GetTotalAllocatedBytes(false) - allAllocated;
    if (analysis.Completeness != AnalysisCompleteness.Complete || analysis.TotalDiagnosticCount != fixture.ExpectedDiagnostics)
        throw new InvalidOperationException($"Unexpected {phase} result: {analysis.Completeness}, {analysis.TotalDiagnosticCount}, expected {fixture.ExpectedDiagnostics}.");
    var state = Inspect(session);
    if (phase != "cold-full" && document.Snapshot.Length > 1048576 && state.Count != 0 &&
        (state["LastVisitedUnits"] is null || Convert.ToInt64(state["LastVisitedUnits"]) > 524288 || Convert.ToInt32(state["ArrayDirtyPageCount"]) != 0 ||
         Convert.ToInt64(state["ArrayCertificateVersion"]) != document.Snapshot.Version))
        throw new InvalidOperationException("Ordinary owner-local edit exceeded its hard work/version contract.");
    return new
    {
        phase, elapsedMs = elapsed, allocatedBytes = allocation, allocatedAllThreadsBytes = allocationAll,
        completeness = analysis.Completeness.ToString(), diagnosticCount = analysis.TotalDiagnosticCount,
        version = document.Snapshot.Version, projectionStart = range.Start, projectionUnits = range.Length,
        state
    };
}

/// <summary>Reads optional implementation work counters without mutating state or charging reflection to analysis.</summary>
static Dictionary<string, object?> Inspect(IFormatSession session)
{
    var result = new Dictionary<string, object?>();
    foreach (var property in session.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
    {
        if (property.Name is not ("LastVisitedUnits" or "ArrayPageCount" or "ArrayDirtyPageCount" or
            "ArrayCertificateVersion" or "ArrayCertificateRetainedBytes")) continue;
        var value = property.GetValue(session);
        result[property.Name] = value;
    }
    return result;
}

/// <summary>Identity and exact edit geometry for a deterministic ASCII UTF-8 corpus.</summary>
sealed record Fixture(string Kind, string Newline, int MiB, int Seed, long FileBytes, long Utf16Units,
    string Sha256, long Records, int HeaderUnits, int RecordStride, int EditOffset, int ExpectedDiagnostics);

/// <summary>Attributes source/history retention by identity, outside analysis and post-GC memory measurements.</summary>
static class EngineGraph
{
    /// <summary>Counts unique immutable rope nodes and strings reachable from the current snapshot and Undo/Redo.</summary>
    internal static object Inspect(Document document)
    {
        var rootProperty = typeof(TextSnapshot).GetProperty("Root", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var root = rootProperty.GetValue(document.Snapshot)!;
        var nodeType = root.GetType();
        var text = nodeType.GetProperty("Text", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var left = nodeType.GetProperty("Left", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var right = nodeType.GetProperty("Right", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pending = new Stack<object>(); pending.Push(root);
        var historyCount = 0;
        foreach (var field in new[] { "_undo", "_redo" })
        {
            var history = (System.Collections.IEnumerable)typeof(Document).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document)!;
            foreach (var entry in history)
            {
                historyCount++;
                foreach (var name in new[] { "BeforeRoot", "AfterRoot" })
                    if (entry.GetType().GetProperty(name)!.GetValue(entry) is { } previous) pending.Push(previous);
            }
        }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var strings = new HashSet<string>(ReferenceEqualityComparer.Instance);
        long units = 0;
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            if (text.GetValue(node) is string chars)
            {
                if (strings.Add(chars)) units += chars.Length;
                continue;
            }
            if (left.GetValue(node) is { } a) pending.Push(a);
            if (right.GetValue(node) is { } b) pending.Push(b);
        }
        return new { historyCount, uniqueNodes = seen.Count, uniqueStrings = strings.Count, uniqueUtf16Units = units };
    }
}

/// <summary>Measures equal-shaped certificate graph allocation, rather than assuming native bool/struct packing.</summary>
static class CertificateLayout
{
    /// <summary>Reports actual shallow certificate plus page-array clone bytes, excluding source and fixed session state.</summary>
    internal static object? Measure(IFormatSession session)
    {
        var certificate = session.GetType().GetField("_array", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session);
        if (certificate is null) return null;
        var pages = (Array)certificate.GetType().GetProperty("Pages")!.GetValue(certificate)!;
        var clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<object, object>>();
        GC.KeepAlive(clone(certificate));
        GC.KeepAlive(pages.Clone());
        var before = GC.GetAllocatedBytesForCurrentThread();
        var copiedCertificate = clone(certificate);
        var copiedPages = pages.Clone();
        var graphBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var stride = (int)typeof(CertificateLayout).GetMethod(nameof(Stride), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(pages.GetType().GetElementType()!).Invoke(null, null)!;
        GC.KeepAlive(copiedCertificate); GC.KeepAlive(copiedPages);
        return new { pages = pages.Length, managedPageStrideBytes = stride, graphBytes };
    }

    /// <summary>Uses the runtime's managed element stride, not Marshal's interop layout.</summary>
    private static int Stride<T>() => Unsafe.SizeOf<T>();
}

/// <summary>Generates complete exact-byte-sized arrays; never truncates JSON or stores a whole corpus string.</summary>
static class Corpus
{
    /// <summary>Creates ordinary LF/CRLF and compact primitive files at each declared size.</summary>
    internal static void GenerateAll()
    {
        Directory.CreateDirectory(".temp/JsonArrayPages/corpora");
        foreach (var mib in new[] { 1, 10, 100 })
        {
            Generate("ordinary", "lf", mib);
            Generate("ordinary", "crlf", mib);
            Generate("primitive", "none", mib);
        }
    }

    /// <summary>Leaves one final string to adjust exact size while preserving every preceding complete value.</summary>
    private static void Generate(string kind, string newline, int mib)
    {
        const int seed = 64517;
        var path = $".temp/JsonArrayPages/corpora/{kind}-{newline}-{mib}.json";
        var nl = newline switch { "lf" => "\n", "crlf" => "\r\n", _ => "" };
        var header = "[" + nl;
        var prefix = "{\"id\":64517,\"nested\":{\"a\":1,\"\\u0061\":2,\"list\":[true,false,null,-1.25e+3,{\"q\":\"escaped \\\" ,]} \\\\ \\ud83d\\ude00\"}]},\"pad\":\"";
        var record = kind == "primitive" ? "0" : prefix + new string('a', 512 - prefix.Length - 2) + "\"}";
        var stride = record + "," + nl;
        var target = mib * 1048576L;
        var recordCount = (target - header.Length - 3) / stride.Length;
        var remainder = checked((int)(target - header.Length - recordCount * stride.Length - 3));
        using (var writer = new StreamWriter(path, false, new UTF8Encoding(false), 65536))
        {
            writer.Write(header);
            for (long i = 0; i < recordCount; i++) writer.Write(stride);
            writer.Write('"'); writer.Write(new string('a', remainder)); writer.Write("\"]");
        }
        using var input = File.OpenRead(path);
        if (input.Length != target) throw new InvalidOperationException("Corpus byte accounting failed.");
        var metadata = new Fixture(kind, newline, mib, seed, target, target,
            Convert.ToHexString(SHA256.HashData(input)), recordCount, header.Length, stride.Length,
            kind == "primitive" ? 0 : prefix.Length + 20, kind == "primitive" ? 0 : checked((int)recordCount));
        File.WriteAllText(path + ".metadata.json", JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Generated {path}: {metadata.FileBytes} bytes, {recordCount} records, SHA256 {metadata.Sha256}");
    }
}
