using System.Diagnostics;
using System.Globalization;
using System.Text;
using Mote.Engine;
using Mote.Formats;

/// <summary>Measures CSV index and projection costs without claiming heap retention or Native AOT performance.</summary>
/// <remarks>
/// Arguments: nominal million-binary UTF-16 units (3..100), warm repetitions (1..100), fixture, cold repetitions (1..100).
/// Fixture construction and baseline indexing are excluded from timed edit/projection routes.
/// Cold means a fresh format-session cache, not a fresh process or an application-startup measurement.
/// Allocations are current-thread cumulative allocations; RSS is process-wide working set.
/// The retained-index column is the session's accounting estimate, not a heap graph measurement.
/// </remarks>
internal static class Program
{
    private sealed record Sample(double Milliseconds, long Allocation, long Rss,
        string Mode, int Segments, int Checkpoints, long EstimatedIndexBytes, long? Version, long ScannedUnits);

    /// <summary>Runs reproducible scenarios and writes invariant-culture CSV to standard output.</summary>
    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var mib = args.Length == 0 ? 3 : int.Parse(args[0]);
        var repetitions = args.Length < 2 ? 9 : int.Parse(args[1]);
        var fixture = args.Length < 3 ? "ordinary" : args[2];
        var coldRepetitions = args.Length < 4 ? Math.Min(repetitions, 3) : int.Parse(args[3]);
        if (mib is < 3 or > 100 || repetitions is < 1 or > 100 || coldRepetitions is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(args), "Use 3..100 nominal UTF-16 Mi-units and 1..100 repetitions.");
        var source = CreateSource(mib * 1024 * 1024, fixture);
        Console.WriteLine("fixture,nominal_utf16_mi_units,utf16_chars,text_utf16_bytes,repetitions,route,p50_ms,p95_ms,mean_alloc_bytes,max_process_rss_bytes,index_mode,segments,checkpoints,estimated_retained_index_bytes,version,mean_last_call_scanned_source_units");
        ColdRoutes();
        WarmRoutes();
        foreach (var location in new[] { "head", "middle", "tail" }) EditRoutes(location);
        return 0;

        void ColdRoutes()
        {
            var full = new List<Sample>();
            var prefix = new List<Sample>();
            var resumed = new List<Sample>();
            for (var i = 0; i < coldRepetitions; i++)
            {
                using var document = new Document(source);
                var snapshot = document.Snapshot;
                using (var session = NewSession())
                    full.Add(Measure(session, () => Analyze(session, snapshot, AnalysisScope.Full)));
                using var prefixSession = NewSession();
                prefix.Add(Measure(prefixSession, () => Analyze(prefixSession, snapshot, AnalysisScope.Visible)));
                resumed.Add(Measure(prefixSession, () => Analyze(prefixSession, snapshot, AnalysisScope.Full)));
            }
            Print("cold_full", full);
            Print("cold_prefix_visible", prefix);
            Print("prefix_to_full", resumed);
        }

        void WarmRoutes()
        {
            using var document = new Document(source);
            using var session = NewSession();
            var snapshot = document.Snapshot;
            Analyze(session, snapshot, AnalysisScope.Full);
            var head = Window(snapshot.Length, 0);
            var tail = Window(snapshot.Length, snapshot.Length - 80);
            Warm("legacy_one", () => session.Analyze(snapshot, [], new AnalysisRequest(head, AnalysisScope.Visible)));
            Warm("windowed_one", () => session.AnalyzeWindows(snapshot, [], [head], AnalysisScope.Visible));
            Warm("two_calls", () =>
            {
                session.Analyze(snapshot, [], new AnalysisRequest(head, AnalysisScope.Visible));
                session.Analyze(snapshot, [], new AnalysisRequest(tail, AnalysisScope.Visible));
            });
            Warm("windowed_two", () => session.AnalyzeWindows(snapshot, [], [head, tail], AnalysisScope.Visible));

            void Warm(string route, Action action)
            {
                action();
                var samples = new List<Sample>();
                for (var i = 0; i < repetitions; i++) samples.Add(Measure(session, action));
                Print(route, samples);
            }
        }

        void EditRoutes(string location)
        {
            var visible = new List<Sample>();
            var full = new List<Sample>();
            for (var i = 0; i < coldRepetitions; i++)
            {
                using var document = new Document(source);
                using var session = NewSession();
                var before = document.Snapshot;
                Analyze(session, before, AnalysisScope.Full);
                var offset = location switch { "head" => 0, "middle" => source.Length / 2, _ => source.Length - 1 };
                var edit = new TextChange(offset, 0, "x");
                TextSnapshot? after = null;
                visible.Add(Measure(session, () =>
                {
                    after = document.Apply(edit);
                    session.AnalyzeWindows(after, [new VersionedEdit(before.Version, after.Version, edit)],
                        [Window(after.Length, offset)], AnalysisScope.Visible);
                }));
                full.Add(Measure(session, () => Analyze(session, after!, AnalysisScope.Full)));
            }
            Print($"edit_{location}_apply_visible", visible);
            Print($"edit_{location}_subsequent_full", full);
        }

        void Print(string route, List<Sample> samples)
        {
            var sorted = samples.Select(sample => sample.Milliseconds).Order().ToArray();
            var last = samples[^1];
            Console.WriteLine($"{fixture},{mib},{source.Length},{2L * source.Length},{samples.Count},{route}," +
                $"{sorted[(samples.Count - 1) / 2]:F4},{sorted[(int)Math.Ceiling(samples.Count * .95) - 1]:F4}," +
                $"{samples.Sum(sample => sample.Allocation) / samples.Count},{samples.Max(sample => sample.Rss)}," +
                $"{last.Mode},{last.Segments},{last.Checkpoints},{last.EstimatedIndexBytes},{last.Version}," +
                $"{samples.Sum(sample => sample.ScannedUnits) / samples.Count}");
        }
    }

    /// <summary>Creates a fresh document-local analyzer; the friend assembly exposes diagnostic accounting only.</summary>
    private static CsvIncrementalSession NewSession() => (CsvIncrementalSession)new CsvPolicy().CreateSession();

    /// <summary>Requests source-start projection plus the desired index breadth, checking full-result truthfulness.</summary>
    private static void Analyze(CsvIncrementalSession session, TextSnapshot snapshot, AnalysisScope scope)
    {
        var result = session.AnalyzeWindows(snapshot, [], [Window(snapshot.Length, 0)], scope);
        if (scope == AnalysisScope.Full && (result.Completeness != AnalysisCompleteness.Complete || result.TotalDiagnosticCount is null))
            throw new InvalidOperationException("Full analysis did not provide exact whole-document semantics.");
    }

    /// <summary>Creates a bounded interval valid even when the requested origin is near EOF.</summary>
    private static TextSpan Window(int length, int origin)
    {
        var start = Math.Clamp(origin, 0, length);
        return new TextSpan(start, Math.Min(80, length - start));
    }

    /// <summary>Times only the supplied action; RSS sampling and cache-accounting reads happen afterward.</summary>
    private static Sample Measure(CsvIncrementalSession session, Action action)
    {
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocation = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var statistics = session.CacheStatistics;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new Sample(elapsed, allocation, process.WorkingSet64, statistics.Mode,
            statistics.SegmentCount, statistics.CheckpointCount, statistics.EstimatedRetainedIndexBytes,
            statistics.Version, statistics.ScannedSourceUnits);
    }

    /// <summary>Builds exact-size UTF-16 fixtures; truncation may intentionally leave one malformed final record.</summary>
    private static string CreateSource(int length, string fixture)
    {
        if (fixture == "quoted") return "\"" + new string('a', length - 2) + "\"";
        if (fixture == "distinct-width") return CreateDistinctWidthSource(length);
        var pattern = fixture switch
        {
            "ordinary" => "first,second,third,quoted,12345,abcdefghijklmno\r\n",
            "dense" => "a\n",
            _ => throw new ArgumentException("Fixture must be ordinary, dense, quoted, or distinct-width.", nameof(fixture))
        };
        var builder = new StringBuilder(length);
        while (builder.Length < length) builder.Append(pattern.AsSpan(0, Math.Min(pattern.Length, length - builder.Length)));
        return builder.ToString();
    }

    /// <summary>Uses successively wider records rather than a small repeating width histogram.</summary>
    private static string CreateDistinctWidthSource(int length)
    {
        var builder = new StringBuilder(length);
        for (var width = 1; builder.Length < length; width++)
        {
            var record = string.Join(',', Enumerable.Repeat("a", width)) + "\n";
            builder.Append(record.AsSpan(0, Math.Min(record.Length, length - builder.Length)));
        }
        return builder.ToString();
    }
}
