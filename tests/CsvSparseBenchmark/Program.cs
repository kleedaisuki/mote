using System.Diagnostics;
using Mote.Engine;
using Mote.Formats;

/// <summary>Measures warm CSV projection costs, excluding fixture construction and full indexing.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var targetMiB = args.Length == 0 ? 3 : int.Parse(args[0]);
        var repetitions = args.Length < 2 ? 9 : int.Parse(args[1]);
        if (targetMiB is < 3 or > 100 || repetitions is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(args), "Use 3..100 MiB and 1..100 repetitions.");
        const string line = "first,second,third,quoted,12345,abcdefghijklmno\r\n";
        var source = "a,b,c,d,e,f\r\n" + string.Concat(Enumerable.Repeat(line,
            (targetMiB * 1024 * 1024) / line.Length + 1));
        using var document = new Document(source);
        using var session = (IWindowedFormatSession)new CsvPolicy().CreateSession();
        var snapshot = document.Snapshot;
        var head = new TextSpan(0, 80);
        var tail = new TextSpan(source.Length - 80, 80);
        var complete = session.AnalyzeWindows(snapshot, [], [head], AnalysisScope.Full);
        if (complete.Completeness != AnalysisCompleteness.Complete || complete.TotalDiagnosticCount != 0)
            throw new InvalidOperationException("Benchmark fixture did not validate.");

        var old = Measure(repetitions, () => session.Analyze(snapshot, [],
            new AnalysisRequest(head, AnalysisScope.Visible)));
        var one = Measure(repetitions, () => session.AnalyzeWindows(snapshot, [],
            [head], AnalysisScope.Visible));
        var twoCalls = Measure(repetitions, () =>
        {
            session.Analyze(snapshot, [], new AnalysisRequest(head, AnalysisScope.Visible));
            session.Analyze(snapshot, [], new AnalysisRequest(tail, AnalysisScope.Visible));
        });
        var batch = Measure(repetitions, () =>
        {
            var result = session.AnalyzeWindows(snapshot, [], [head, tail], AnalysisScope.Visible);
            if (result.Root.Children.Count < 2 || result.TotalDiagnosticCount != 0)
                throw new InvalidOperationException("Sparse projection lost a window or the exact count.");
        });
        Console.WriteLine("miB,utf16,repetitions,route,p50_ms,p95_ms,mean_alloc_bytes");
        Print("legacy_one", old);
        Print("windowed_one", one);
        Print("two_calls", twoCalls);
        Print("windowed_two", batch);
        return 0;

        void Print(string route, (double P50, double P95, long Allocation) result) =>
            Console.WriteLine($"{targetMiB},{snapshot.Length},{repetitions},{route},{result.P50:F4},{result.P95:F4},{result.Allocation}");
    }

    private static (double P50, double P95, long Allocation) Measure(int repetitions, Action action)
    {
        action();
        var milliseconds = new double[repetitions];
        long allocated = 0;
        for (var i = 0; i < repetitions; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            action();
            timer.Stop();
            milliseconds[i] = timer.Elapsed.TotalMilliseconds;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(milliseconds);
        return (milliseconds[(repetitions - 1) / 2],
            milliseconds[(int)Math.Ceiling(repetitions * .95) - 1], allocated / repetitions);
    }
}
