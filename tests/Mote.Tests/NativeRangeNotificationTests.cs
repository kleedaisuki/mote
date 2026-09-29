using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Viewport;

namespace Mote.Tests;

/// <summary>Independent range-only notification checks for native navigation and analysis consumers.</summary>
public sealed class NativeRangeNotificationTests
{
    /// <summary>Range transforms match the legacy payload path, including reverse selections and Undo/Redo.</summary>
    [Fact]
    public void Range_navigation_and_viewport_match_legacy_change_transforms()
    {
        using var document = new Document("abcdef\nuvwxyz");
        var rangeNav = new NativeNavigationModel();
        var legacyNav = new NativeNavigationModel();
        rangeNav.SetSelection(document.Snapshot, 10, 3);
        legacyNav.SetSelection(document.Snapshot, 10, 3);
        var rangeCanvas = new CanvasInteraction(document.Snapshot, 18, 80, 4096, rangeNav);
        var legacyCanvas = new CanvasInteraction(document.Snapshot, 18, 80, 4096, legacyNav);
        rangeCanvas.Reveal(9);
        legacyCanvas.Reveal(9);
        var delivered = new List<string>();
        document.ChangedRange += (_, e) =>
        {
            delivered.Add($"range:{e.After.Version}");
            rangeNav.ApplyChange(e.Change, e.After);
            rangeCanvas.ApplyEdit(e.After, e.Change);
        };
        document.Changed += (_, e) =>
        {
            delivered.Add($"legacy:{e.After.Version}");
            legacyNav.ApplyChange(e.Change, e.After);
            legacyCanvas.ApplyEdit(e.After, e.Change);
        };

        document.Apply(new TextChange(2, 0, "XX"));
        AssertParity();
        document.Apply(new TextChange(5, 4, "Z"));
        AssertParity();
        Assert.True(document.Undo());
        AssertParity();
        Assert.True(document.Redo());
        AssertParity();

        Assert.Equal(8, delivered.Count);
        for (var i = 0; i < delivered.Count; i += 2)
        {
            Assert.StartsWith("range:", delivered[i]);
            Assert.Equal("legacy:" + delivered[i][6..], delivered[i + 1]);
        }

        void AssertParity()
        {
            Assert.Equal((legacyNav.Anchor, legacyNav.Active), (rangeNav.Anchor, rangeNav.Active));
            var actual = rangeCanvas.Frame();
            var oracle = legacyCanvas.Frame();
            Assert.Equal(document.Snapshot.Version, actual.Version);
            Assert.Equal(oracle.TopAnchor, actual.TopAnchor);
            Assert.Equal(oracle.SelectionAnchor, actual.SelectionAnchor);
            Assert.Equal(oracle.SelectionActive, actual.SelectionActive);
            Assert.Equal(oracle.Slices.Select(s => (s.SourceStart, s.SourceLength)),
                actual.Slices.Select(s => (s.SourceStart, s.SourceLength)));
            Assert.All(actual.Slices, slice => Assert.InRange(slice.SourceLength, 0, 4096));
        }
    }

    /// <summary>Only bounded edits enter the session log; a giant insertion rebuilds without copying its payload.</summary>
    [Fact]
    public async Task Range_session_driver_keeps_small_insert_and_rebuilds_after_large_insert()
    {
        using var document = new Document("abc");
        var policy = new RecordingPolicy();
        using var driver = new NativeFormatSessionDriver(policy);
        var request = new AnalysisRequest(new TextSpan(0, 1), AnalysisScope.Visible);
        await driver.AnalyzeAsync(document.Snapshot, request, CancellationToken.None);
        var recordAutomatically = true;
        DocumentChangedRangeEventArgs? captured = null;
        document.ChangedRange += (_, e) =>
        {
            captured = e;
            if (recordAutomatically) driver.Record(e);
        };

        document.Apply(new TextChange(1, 0, "X"));
        await driver.AnalyzeAsync(document.Snapshot, request, CancellationToken.None);
        var small = Assert.Single(policy.LastChanges);
        Assert.Equal("X", small.Change.InsertText);
        Assert.Equal((0L, 1L), (small.BeforeVersion, small.AfterVersion));

        recordAutomatically = false;
        document.Apply(new TextChange(2, 0, new string('Y', 1024 * 1024 + 1)));
        Assert.NotNull(captured);
        var before = GC.GetAllocatedBytesForCurrentThread();
        driver.Record(captured);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.InRange(allocated, 0, 1024 * 1024);
        await driver.AnalyzeAsync(document.Snapshot, request, CancellationToken.None);
        Assert.True(policy.CreatedSessions >= 2, "Version gap after a giant insert must rebuild parser state.");
        Assert.Empty(policy.LastChanges);
    }

    /// <summary>A minimal policy that records the exact versioned edits passed to its newest session.</summary>
    private sealed class RecordingPolicy : IIncrementalDocumentPolicy
    {
        public DocumentKind Kind => DocumentKind.PlainText;
        public string DisplayName => "Range recording policy";
        public int CreatedSessions { get; private set; }
        public IReadOnlyList<VersionedEdit> LastChanges { get; private set; } = [];

        public IFormatSession CreateSession()
        {
            CreatedSessions++;
            return new RecordingSession(this);
        }

        public FormatAnalysis Analyze(string text, CancellationToken cancellationToken = default) =>
            new(text, new SemanticNode("document", new TextSpan(0, text.Length)), [], []);
        public string Format(string text) => text;
        public string RenderHtml(FormatAnalysis analysis) => string.Empty;

        private sealed class RecordingSession(RecordingPolicy owner) : IFormatSession
        {
            public DocumentAnalysis Analyze(TextSnapshot snapshot, IReadOnlyList<VersionedEdit> changesSinceCommittedState,
                AnalysisRequest request, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                owner.LastChanges = changesSinceCommittedState.ToArray();
                return new DocumentAnalysis(snapshot.Version, new TextSpan(0, snapshot.Length),
                    AnalysisCompleteness.Complete,
                    new SemanticNode("document", new TextSpan(0, snapshot.Length)), [], [], 0);
            }

            public void Dispose() { }
        }
    }
}
