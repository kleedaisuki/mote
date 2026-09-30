using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Security.Cryptography;
using Mote.Engine;
using Mote.Formats;

var mode = args.ElementAtOrDefault(0) ?? "flat";
var mib = int.Parse(args.ElementAtOrDefault(1) ?? "100");
if (mode is not ("flat" or "unique" or "adjacent" or "fence" or "reference") || mib is < 1 or > 1024 || args.Length > 3)
    throw new ArgumentException("Usage: flat|unique|adjacent|fence|reference [1..1024 MiB] [attribution]");
if (args.Length == 3 && (args[2] != "attribution" || mode != "flat"))
    throw new ArgumentException("Warm verifier attribution is supported only for the flat corpus.");
if (!File.Exists("mote.sln"))
    throw new InvalidOperationException("Run this benchmark from the mote repository root; corpora stay under .temp.");
var root = Path.GetFullPath(".temp/MarkdownFlatColdAllocationProbe");
Directory.CreateDirectory(root);
var path = Path.Combine(root, $"corpus-{mode}-{mib}.md");
if (!File.Exists(path))
{
    using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
    if (mode == "reference") writer.Write("[b]: https://first.test\n\n[d]: javascript&#58;bad\n\n");
    var count = 0;
    for (long wrote = 0; wrote < mib * 1048576L; count++)
    {
        var owner = mode switch
        {
            "unique" => "Alpha " + count + " " + new string('a', 4000) + " End\n\n",
            "adjacent" => "# Heading\nAlpha " + new string('a', 4000) + " End\n",
            "fence" => "```json\n" + new string('a', 4000) + "\n```\n\n",
            "reference" => "Alpha [a][b] [c][d] " + new string('a', 4000) + " End\n\n",
            _ => "Alpha " + new string('a', 4000) + " End\n\n"
        };
        writer.Write(owner);
        wrote += owner.Length;
    }
}
using var document = await Document.OpenAsync(path);
using var session = new MarkdownPolicy().CreateSession();
if (args.ElementAtOrDefault(2) == "attribution")
{
    var start = document.Snapshot.GetLineStartOffset(0);
    var end = document.Snapshot.GetLineStartOffset(1);
    var verify = typeof(MarkdownPolicy).Assembly.GetType("Mote.Formats.MarkdownIncrementalSession")!
        .GetMethod("IsVerifiedFlatBlock", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Verify>();
    var raw = document.Snapshot.GetText(start, end - start);
    var body = raw[..^1];
    verify(body, out _);
    const int repeats = 3000;
    var stamp = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < repeats; i++) GC.KeepAlive(document.Snapshot.GetText(start, end - start));
    var rawBytes = GC.GetAllocatedBytesForCurrentThread() - stamp;
    stamp = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < repeats; i++) GC.KeepAlive(raw[..^1]);
    var sliceBytes = GC.GetAllocatedBytesForCurrentThread() - stamp;
    stamp = GC.GetAllocatedBytesForCurrentThread();
    for (var i = 0; i < repeats; i++) if (!verify(body, out _)) throw new Exception("Unexpected verifier refusal.");
    var parserBytes = GC.GetAllocatedBytesForCurrentThread() - stamp;
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "attribution", repeats, rawBytes, sliceBytes, parserBytes, rawUnits = raw.Length, bodyUnits = body.Length }));
    WriteInput();
    return;
}
var range = new TextSpan(document.Snapshot.Length / 2, 1);
var process = Process.GetCurrentProcess();
var coldVisible = Measure("cold-visible", [], AnalysisScope.Visible);
var liveBefore = GC.GetTotalMemory(true);
var full = Measure("cold-full", [], AnalysisScope.Full);
var liveAfter = GC.GetTotalMemory(true);
process.Refresh();
Console.WriteLine(JsonSerializer.Serialize(new { mode, mib, phase = "after-full", source = document.Snapshot.Length,
    liveDelta = liveAfter - liveBefore, working = process.WorkingSet64, peakWorking = process.PeakWorkingSet64 }));
for (var i = 0; i < 10; i++)
{
    var before = document.Snapshot;
    var edit = mode == "reference"
        ? new TextChange(5, before.GetLine(0).Length - 5, i % 2 == 0 ? "javascript&#58;changed" : "https://different.test/longer")
        : new TextChange(mode == "fence" ? 12 : 20, 1, i % 2 == 0 ? "X" : "Y");
    var after = document.Apply(edit);
    Measure("edit-" + i, [new(before.Version, after.Version, edit)], AnalysisScope.Visible);
}
GC.KeepAlive(full);
GC.KeepAlive(coldVisible);
WriteInput();

/// <summary>Records fixture identity after measurement, without pre-warming analysis or JSON serialization.</summary>
void WriteInput()
{
    using var input = File.OpenRead(path);
    Console.WriteLine(JsonSerializer.Serialize(new { phase = "input", mode, mib,
        sha256 = Convert.ToHexString(SHA256.HashData(input)), fileBytes = input.Length,
        runtime = Environment.Version.ToString(), os = Environment.OSVersion.ToString() }));
}

/// <summary>Times analysis only; file opening, forced GC and result serialization stay outside the interval.</summary>
DocumentAnalysis Measure(string phase, IReadOnlyList<VersionedEdit> edits, AnalysisScope scope)
{
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var allThreads = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    var analysis = session.Analyze(document.Snapshot, edits, new(range, scope));
    watch.Stop();
    var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var totalBytes = GC.GetTotalAllocatedBytes(true) - allThreads;
    Console.WriteLine(JsonSerializer.Serialize(new { mode, mib, phase, ms = watch.Elapsed.TotalMilliseconds,
        allocated = bytes, totalAllocated = totalBytes, completeness = analysis.Completeness.ToString(),
        total = analysis.TotalDiagnosticCount, source = document.Snapshot.Length,
        nodes = analysis.Root.Children.Count, tokens = analysis.Tokens.Count }));
    return analysis;
}

/// <summary>Matches the baseline private verifier without reflection invocation allocation.</summary>
delegate bool Verify(string source, out int level);
