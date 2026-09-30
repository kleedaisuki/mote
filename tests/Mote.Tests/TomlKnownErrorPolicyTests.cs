using Mote.Engine;
using Mote.Formats;

namespace Mote.Tests;

/// <summary>Large-file TOML first-witness contracts without any global completion claim.</summary>
public sealed class TomlKnownErrorPolicyTests
{
    /// <summary>Comments cross the whole-parser threshold without changing ownership.</summary>
    private static readonly string Padding = string.Concat(Enumerable.Repeat("#" + new string('x', 4094) + "\n", 1025));

    /// <summary>Individually valid statements expose exact conflicts after a certified prefix.</summary>
    [Theory]
    [InlineData("a=1\n", "a=2\n", 1)]
    [InlineData("a=1\n", "\"\\u0061\"=2\n", 8)]
    [InlineData("a=1\n", "a.b=2\n", 3)]
    [InlineData("[a]\n", "[a]\n", 1)]
    [InlineData("a={b=1}\n", "a.c=2\n", 3)]
    [InlineData("a.b=1\n", "[a]\n", 1)]
    [InlineData("a=\"\"\"hello\nworld\"\"\"\n", "a=2\n", 1)]
    public void Full_preserves_one_exact_error_and_visible_lexical_output(string prefix, string suffix, int keyLength)
    {
        var source = prefix + Padding + suffix;
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var modifiedBefore = document.IsModified;
        var viewport = new TextSpan(source.Length - suffix.Length, suffix.Length);
        var visible = session.Analyze(document.Snapshot, [], new(viewport, AnalysisScope.Visible));
        var full = session.Analyze(document.Snapshot, [], new(viewport, AnalysisScope.Full));
        var error = Assert.Single(full.Diagnostics);
        Assert.Equal("TOML_OWNERSHIP", error.Code);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(new TextSpan(viewport.Start + (suffix[0] == '[' ? 1 : 0), keyLength), error.Span);
        Assert.Equal(AnalysisCompleteness.Provisional, full.Completeness);
        Assert.Null(full.TotalDiagnosticCount);
        Assert.Equal(document.Snapshot.Version, full.Version);
        Assert.Equal(visible.Coverage, full.Coverage);
        Assert.Equal(visible.Tokens, full.Tokens);
        Assert.Empty(full.Root.Children);
        Assert.Equal(source, document.Snapshot.GetText());
        Assert.Equal(modifiedBefore, document.IsModified);
        Assert.NotEmpty(new TomlPolicy().Analyze(prefix + suffix).Diagnostics);
    }

    /// <summary>An invalid suffix cannot erase an already proved conflict or inflate its count.</summary>
    [Fact]
    public void Stops_at_first_conflict_before_unknown_suffix_and_keeps_offscreen_fact()
    {
        var source = "a=1\n" + Padding + "a=2\na=3\nb=???\n";
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var result = session.Analyze(document.Snapshot, [], new(new TextSpan(0, 4), AnalysisScope.Full));
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(4 + Padding.Length, error.Span.Start);
        Assert.Equal(AnalysisCompleteness.Provisional, result.Completeness);
        Assert.Null(result.TotalDiagnosticCount);
        Assert.True(error.Span.Start > result.Coverage.End);
    }

    /// <summary>Each new authoritative version reconstructs ownership, including Undo and Redo.</summary>
    [Fact]
    public void Repair_undo_redo_with_missing_history_rebuild_current_witness()
    {
        var source = "a=1\n" + Padding + "a=2\n";
        using var document = new Document(source);
        using var session = new TomlPolicy().CreateSession();
        var viewport = new TextSpan(source.Length - 4, 4);
        DocumentAnalysis Full() => session.Analyze(document.Snapshot, [], new(viewport, AnalysisScope.Full));
        var initial = Full();
        Assert.Single(initial.Diagnostics);
        document.Apply(new TextChange(viewport.Start, 1, "b"));
        var repaired = Full();
        Assert.Empty(repaired.Diagnostics);
        Assert.Equal(AnalysisCompleteness.Complete, repaired.Completeness);
        Assert.Equal(0, repaired.TotalDiagnosticCount);
        Assert.True(document.Undo());
        var restored = Full();
        Assert.True(restored.Version > repaired.Version);
        Assert.Equal(initial.Diagnostics.Single().Span, Assert.Single(restored.Diagnostics).Span);
        Assert.True(document.Redo());
        Assert.Empty(Full().Diagnostics);
    }
}
