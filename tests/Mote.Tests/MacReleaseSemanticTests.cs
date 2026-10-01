using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Mac;

namespace Mote.Tests;

/// <summary>Portable final semantic witnesses. No test creates a native view or substitutes for hosted AppKit evidence.</summary>
public sealed class MacReleaseSemanticTests
{
    /// <summary>A previous version, nonce, publication, coverage or unfinished palette cannot authorize final capture.</summary>
    [Theory]
    [InlineData("current", true)]
    [InlineData("version", false)]
    [InlineData("nonce", false)]
    [InlineData("analysis-version", false)]
    [InlineData("sequence", false)]
    [InlineData("provisional", false)]
    [InlineData("coverage", false)]
    [InlineData("style", false)]
    [InlineData("geometry", false)]
    public void Final_source_requires_exact_complete_current_facts(string mutation, bool accepted)
    {
        using var document = new Document();
        document.Apply(new TextChange(0, 0, "fixture"));
        var installed = Install(document);
        var semantics = new NativeSourceSemantics(installed.Stamp, installed.Nonce, 3,
            AnalysisCompleteness.Complete, new(0, document.Snapshot.Length), [], []);
        var analysis = new NativeAnalysisView([], "", "", "", installed.Stamp, PresentationSequence: 3);
        if (mutation == "version") semantics = semantics with { Stamp = semantics.Stamp with { Version = 0 } };
        if (mutation == "nonce") semantics = semantics with { Nonce = 2 };
        if (mutation == "analysis-version") analysis = analysis with { Stamp = analysis.Stamp with { Version = 0 } };
        if (mutation == "sequence") analysis = analysis with { PresentationSequence = 2 };
        if (mutation == "provisional") semantics = semantics with { Completeness = AnalysisCompleteness.Provisional };
        if (mutation == "coverage") semantics = semantics with { Coverage = new(1, document.Snapshot.Length - 1) };
        var evidence = MacReleaseSemanticModel.Observe(installed, semantics, analysis,
            mutation != "style", mutation != "geometry", null, DocumentKind.PlainText);
        Assert.Equal(accepted, evidence is not null);
    }

    /// <summary>A CSV capture requires actual current nonpending visible cells, not merely complete source semantics.</summary>
    [Theory]
    [InlineData("current", true)]
    [InlineData("missing-frame", false)]
    [InlineData("stale-ready", false)]
    [InlineData("pending-cell", false)]
    public void Csv_requires_current_visible_grid(string mutation, bool accepted)
    {
        using var document = new Document();
        document.Apply(new TextChange(0, 0, "x"));
        var installed = Install(document);
        var semantics = new NativeSourceSemantics(installed.Stamp, 1, 3, AnalysisCompleteness.Complete, new(0, 1), [], []);
        var analysis = new NativeAnalysisView([], "", "", "", installed.Stamp, PresentationSequence: 3);
        var state = mutation == "pending-cell" ? GridValueState.Pending : GridValueState.Complete;
        var row = new GridRow(0, new(0, 1), new(1, 0), 1,
            [new(0, state == GridValueState.Pending ? null : new TextSpan(0, 1),
                new(0, state == GridValueState.Pending ? 0 : 1), state, false)]);
        var projection = new GridRenderProjection(installed.Stamp.Version, 1, new(1, 1, 1),
            AnalysisCompleteness.CoveredRegion, [new(0, 1)], null, new(0, 1), new(0, 1), "x", [row], [], false, false, false, false);
        var grid = new GridAccessibilityFrame(new(installed.Stamp, 1), null,
            mutation == "stale-ready" ? new(installed.Stamp, 2) : analysis.Identity,
            projection, new(0, 1), new(0, 1), null, null, false);
        var evidence = MacReleaseSemanticModel.Observe(installed, semantics, analysis, true, true,
            mutation == "missing-frame" ? null : grid, DocumentKind.Csv);
        Assert.Equal(accepted, evidence is not null);
        if (evidence is not null) { Assert.Equal(1, evidence.GridCells); Assert.Equal(0, evidence.GridPendingCells); }
    }

    /// <summary>Creates an immutable portable source installation with no platform I/O.</summary>
    private static NativeSourceInstallation Install(Document document) => new(document.Snapshot,
        new(1, document.Snapshot.Version), 1, new(document.Snapshot.GetText(), NativeLineEndingMode.Preserve), 0, 0);
}
