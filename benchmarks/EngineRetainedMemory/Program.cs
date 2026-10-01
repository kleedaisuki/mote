using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Mote.Engine;

if (args.Length is < 3 or > 5 || args[0] is not ("inline" or "detached"))
    throw new ArgumentException("Usage: inline|detached <fixture> <output> [edit count, default 5200] [pause]");
var fixture = Path.GetFullPath(args[1]);
var output = Path.GetFullPath(args[2]);
var root = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
if (!fixture.StartsWith(root + ".temp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
    !output.StartsWith(root + ".cache" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Fixture must be under .temp and result must be under .cache.");
var count = args.Length >= 4 ? int.Parse(args[3]) : 5200;
if (count < 512 || count > 10400) throw new ArgumentOutOfRangeException(nameof(count));
var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fixture)));
var opened = Stopwatch.StartNew();
var document = await Document.OpenAsync(fixture);
var openMilliseconds = opened.Elapsed.TotalMilliseconds;
// Yield alone can race the still-unwinding completion thread. The short timer makes the
// independent control's boundary explicit; neither waiting nor collection is timed.
if (args[0] == "detached") await Task.Delay(100);
var samples = new List<object> { Sample(document, "open") };
var allocated = GC.GetAllocatedBytesForCurrentThread();
var timer = Stopwatch.StartNew();
Edit(document, count);
var editMilliseconds = timer.Elapsed.TotalMilliseconds;
var editAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
samples.Add(Sample(document, $"edit-{count}"));
var final = document.Snapshot;
var finalGraph = Graph.Inspect(document, includeHistory: false);
var undoCount = ValidateHistory(document, count);
samples.Add(Sample(document, "undo-redo-validated"));
document.Dispose();
samples.Add(Sample(null, "disposed-current-snapshot-retained"));
var result = new
{
    mode = args[0], fixture, sha256 = hash, edits = count,
    runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    engineAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Document).Assembly.Location))),
    openMilliseconds, editMilliseconds, editAllocated, undoCount, finalGraph, samples
};
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PID={Environment.ProcessId} {output}");
if (args.Length == 5) Console.ReadLine();
GC.KeepAlive(final);

/// <summary>Samples live heap separately from identity graph diagnostics and a compacting full-GC check.</summary>
static object Sample(Document? document, string phase)
{
    var requested = GC.GetTotalMemory(true);
    var before = GC.GetGCMemoryInfo();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    var live = GC.GetTotalMemory(false);
    var gc = GC.GetGCMemoryInfo();
    return new { phase, requested, afterExplicitFullGc = live, heap = gc.HeapSizeBytes,
        fragmented = gc.FragmentedBytes, beforeHeap = before.HeapSizeBytes,
        beforeFragmented = before.FragmentedBytes, graph = document is null ? null : Graph.Inspect(document, true) };
}

/// <summary>Edits equal-length padding in the declared 100 MiB LF corpus without retaining old snapshots.</summary>
[MethodImpl(MethodImplOptions.NoInlining)]
static void Edit(Document document, int count)
{
    if (document.Snapshot.Length != 100 * 1024 * 1024) throw new InvalidOperationException("Expected 100 MiB ASCII fixture.");
    for (var i = 0; i < count; i++) document.Apply(Change(i));
}

/// <summary>Returns the exact source geometry shared with the JSON page benchmark.</summary>
static TextChange Change(int i)
{
    const long records = 204003;
    var record = i < 200 ? new long[] { 1, records / 2, records - 2 }[i % 3] : (long)(i - 200) * 104729 % records;
    return new TextChange(checked((int)(2 + record * 514 + 140)), 1, i % 2 == 0 ? "X" : "Y");
}

/// <summary>Verifies the 512-action suffix and its exact current text survives all Undo/Redo operations.</summary>
[MethodImpl(MethodImplOptions.NoInlining)]
static int ValidateHistory(Document document, int count)
{
    var before = Hash(document.Snapshot);
    var undone = 0;
    while (document.Undo()) undone++;
    if (undone != 512) throw new InvalidOperationException($"Undo suffix was {undone}, expected 512.");
    foreach (var i in Enumerable.Range(count - 512, 512))
    {
        if (!document.Redo()) throw new InvalidOperationException("Missing Redo action.");
        var change = Change(i);
        if (document.Snapshot.GetText(change.Start, 1) != change.InsertText) throw new InvalidOperationException("Redo text mismatch.");
    }
    if (document.CanRedo || Hash(document.Snapshot) != before) throw new InvalidOperationException("Redo final text differs.");
    return undone;
}

/// <summary>Hashes immutable UTF-16 chunks without a whole-file string allocation.</summary>
static string Hash(TextSnapshot snapshot)
{
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    foreach (var chunk in snapshot.GetChunks()) hash.AppendData(MemoryMarshal.AsBytes(chunk.Span));
    return Convert.ToHexString(hash.GetHashAndReset());
}

/// <summary>Attributes only the rope roots owned by one document; reflection runs outside timing and GC samples.</summary>
static class Graph
{
    /// <summary>Counts unique current/history nodes and immutable leaf backing strings by identity.</summary>
    internal static object Inspect(Document document, bool includeHistory)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var root = typeof(TextSnapshot).GetProperty("Root", flags)!.GetValue(document.Snapshot)!;
        var type = root.GetType();
        var text = type.GetProperty("Text", flags)!;
        var left = type.GetProperty("Left", flags)!;
        var right = type.GetProperty("Right", flags)!;
        var stack = new Stack<object>(); stack.Push(root);
        var historyCount = 0;
        if (includeHistory)
            foreach (var name in new[] { "_undo", "_redo" })
                foreach (var entry in (System.Collections.IEnumerable)typeof(Document).GetField(name, flags)!.GetValue(document)!)
                {
                    historyCount++;
                    foreach (var field in new[] { "BeforeRoot", "AfterRoot" })
                        if (entry.GetType().GetProperty(field)!.GetValue(entry) is { } old) stack.Push(old);
                }
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var strings = new HashSet<string>(ReferenceEqualityComparer.Instance);
        long units = 0;
        while (stack.TryPop(out var node))
        {
            if (!seen.Add(node)) continue;
            if (text.GetValue(node) is string chars) { if (strings.Add(chars)) units += chars.Length; continue; }
            if (left.GetValue(node) is { } a) stack.Push(a);
            if (right.GetValue(node) is { } b) stack.Push(b);
        }
        return new { historyCount, uniqueNodes = seen.Count, uniqueStrings = strings.Count, uniqueUtf16Units = units };
    }
}
