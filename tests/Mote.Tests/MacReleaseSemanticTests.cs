using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Mac;
using System.Text.Json;

namespace Mote.Tests;

/// <summary>Portable final semantic witnesses. No test creates a native view or substitutes for hosted AppKit evidence.</summary>
public sealed class MacReleaseSemanticTests
{
    /// <summary>Historical presentation failure metadata cannot label a newer successful version as currently failed.</summary>
    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void Historical_failure_fields_include_explicit_current_identity_match(long failedVersion, bool currentMatch)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            MacReleaseAnalysisFailureEvidence.WriteFields(writer,
                new(new(1, failedVersion), 10, NativeAnalysisFailureCategory.InvalidState, -1), new(1, 4), 10);
            writer.WriteEndObject();
        }
        using var json = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(currentMatch, json.RootElement.GetProperty("analysis_failure_current_match").GetBoolean());
        Assert.Equal(failedVersion, json.RootElement.GetProperty("analysis_failure_version").GetInt64());
        Assert.Equal("InvalidState", json.RootElement.GetProperty("analysis_failure_category").GetString());
        Assert.Equal(8, json.RootElement.EnumerateObject().Count());
    }
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

    /// <summary>Default-renderer evidence does not require experimental AX, but must reflect exact actual installed slots.</summary>
    [Theory]
    [InlineData("current", true)]
    [InlineData("missing-slot", false)]
    [InlineData("different-slot", false)]
    [InlineData("columns-uninstalled", false)]
    [InlineData("shape", false)]
    [InlineData("pending", false)]
    public void Renderer_frame_is_independent_of_ax_but_checks_native_installation(string mutation, bool ready)
    {
        var row = new GridRow(0, new(0, 1), new(1, 0), 1, [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]);
        var projection = new GridRenderProjection(4, 1, new(1, 1, 1), AnalysisCompleteness.Complete,
            [new(0, 1)], 0, new(0, 1), new(0, 1), "x", [row], [], false, false, false, false);
        GridRow? slot = mutation == "missing-slot" ? null : mutation == "different-slot"
            ? new GridRow(0, new(0, 1), new(1, 0), 1, [new(0, new(0, 1), new(0, 1), GridValueState.Complete, false)]) : row;
        var observation = MacReleaseGridModel.Observe(new(new(1, 4), 3), projection, null, [slot], new(0, 1),
            mutation == "columns-uninstalled" ? null : new GridRange(0, 1), 1,
            mutation == "shape" ? 0 : 1, mutation == "pending", false, 1);
        Assert.Equal(ready, observation.Frame is not null);
        Assert.Equal(mutation is "missing-slot" or "different-slot" ? 1 : 0, observation.MissingNativeSlots);
    }

    /// <summary>Creates an immutable portable source installation with no platform I/O.</summary>
    private static NativeSourceInstallation Install(Document document) => new(document.Snapshot,
        new(1, document.Snapshot.Version), 1, new(document.Snapshot.GetText(), NativeLineEndingMode.Preserve), 0, 0);
}
