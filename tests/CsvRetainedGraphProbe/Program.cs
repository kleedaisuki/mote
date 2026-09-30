using System.Runtime.CompilerServices;
using Mote.Engine;
using Mote.Formats;

/// <summary>Roots one CSV session for external CoreCLR dumpheap/objsize inspection, never for Native AOT heap claims.</summary>
/// <remarks>The text owner and returned projection leave a non-inlined frame before full GC. Dumps may still contain source.</remarks>
internal static class Program
{
    /// <summary>The sole intentional instance root; the inspector must measure this session's transitive object graph.</summary>
    internal static IFormatSession? HeldSession;

    /// <summary>Accepts dense, widths, or sparse, prints a content-free PID, and waits up to three minutes for collection.</summary>
    private static void Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("dense" or "widths" or "sparse"))
            throw new ArgumentException("Use dense, widths, or sparse.", nameof(args));
        Build(args[0]);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Console.WriteLine($"READY {Environment.ProcessId}");
        Thread.Sleep(TimeSpan.FromMinutes(3));
        GC.KeepAlive(HeldSession);
        HeldSession!.Dispose();
    }

    /// <summary>Creates the indexed fixture then releases caller-owned text and projection references before collection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Build(string mode)
    {
        var source = mode switch
        {
            "dense" => string.Concat(Enumerable.Repeat("a\n", 262_144)),
            "widths" => string.Concat(Enumerable.Range(1, 1024).Select(i => new string(',', i - 1) + "\n")),
            _ => "a,b,c,d,e,f\r\n" + string.Concat(Enumerable.Repeat(
                "first,second,third,quoted,12345,abcdefghijklmno\r\n", 104_857_600 / 49 + 1))
        };
        using var document = new Document(source);
        HeldSession = new CsvPolicy().CreateSession();
        var result = HeldSession.Analyze(document.Snapshot, [],
            new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Full));
        if (result.Completeness != AnalysisCompleteness.Complete)
            throw new InvalidOperationException("Retained-graph fixture did not finish whole-file validation.");
    }
}
