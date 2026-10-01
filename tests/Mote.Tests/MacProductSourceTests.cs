using Mote.Engine;
using Mote.Formats;
using Mote.Native;
using Mote.Native.Mac;
using Mote.Themes;

namespace Mote.Tests;

/// <summary>Portable AppKit source contracts; these tests make no native calls or physical input claims.</summary>
public sealed class MacProductSourceTests
{
    /// <summary>An ordinary sorted native selection does not certify either active endpoint.</summary>
    [Fact]
    public void Ordered_selection_has_no_guessed_direction()
    {
        Assert.Equal(new NativeSourceSelection(2, 5), MacProductSourceModel.Selection(2, 3, 8));
        Assert.Equal(new NativeSourceSelection(5, 5, 5), MacProductSourceModel.Selection(5, 0, 8));
        Assert.Equal(new NativeSourceSelection(2, 5, 2), MacProductSourceModel.Selection(2, 3, 8, new(2, 5, 2)));
        Assert.Null(MacProductSourceModel.Selection(2, 3, 8, new(1, 5, 1)).Active);
    }

    /// <summary>Native not-found/overflow ranges are rejected, not clamped into successful observations.</summary>
    [Fact]
    public void Invalid_native_ranges_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MacProductSourceModel.Selection(nuint.MaxValue, 0, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacProductSourceModel.Selection(7, 2, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacProductSourceModel.Visible(7, 2, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => MacProductSourceModel.Visible(0,
            MacProductSourceModel.MaximumVisibleCharacters + 1, 100_000));
    }

    /// <summary>Rejected imports cannot advertise partial editable replicas.</summary>
    [Theory]
    [InlineData("nul")]
    [InlineData("version")]
    [InlineData("nonce")]
    [InlineData("projection")]
    [InlineData("scalar")]
    public void Installation_rejects_uncertified_source(string defect)
    {
        using var document = new Document(defect == "nul" ? "a\0b" : "a😀b");
        var install = Install(document);
        install = defect switch
        {
            "version" => install with { Stamp = new(1, 5) },
            "nonce" => install with { Nonce = 0 },
            "projection" => install with { Projection = new("partial", NativeLineEndingMode.Preserve) },
            "scalar" => install with { Anchor = 2 },
            _ => install
        };
        Assert.ThrowsAny<Exception>(() => MacProductSourceModel.ValidateInstallation(install));
    }

    /// <summary>A small engine replacement is validated against the complete predecessor and successor, including mixed newlines.</summary>
    [Fact]
    public void Guarded_replacement_keeps_exact_complete_source()
    {
        using var document = new Document("甲\r\n乙\n😀尾");
        var before = Install(document);
        document.Apply(new TextChange(4, 0, "新"));
        var after = Install(document);
        var replacement = new NativeSourceReplacement(before, after, 4, 0, "新");
        MacProductSourceModel.ValidateReplacement(replacement);
        Assert.Throws<ArgumentException>(() => MacProductSourceModel.ValidateReplacement(replacement with { DisplayStart = 3 }));
        Assert.Throws<ArgumentException>(() => MacProductSourceModel.ValidateReplacement(replacement with
            { After = after with { Nonce = 2 } }));
        Assert.Throws<ArgumentException>(() => MacProductSourceModel.ValidateReplacement(replacement with
            { DisplayInsert = "wrong" }));
    }

    /// <summary>Visible styles are source-absolute, clipped, ordered, coalesced, and neutral in uncovered locations.</summary>
    [Fact]
    public void Foreground_decorates_only_native_visible_characters()
    {
        var theme = ThemePolicies.Get(ThemePolicies.DefaultId);
        var projection = new NativeTextProjection(new string('x', 100_000), NativeLineEndingMode.Preserve);
        var semantics = new NativeSourceSemantics(new(1, 0), 1, 1, AnalysisCompleteness.Complete,
            new(0, 100_000), [new("string", new(0, 100_000)), new("number", new(50003, 2))], []);
        var runs = MacProductSourceModel.Foreground(projection, new(50000, 8), semantics, theme);
        Assert.Equal(8, runs.Sum(run => run.Length));
        Assert.All(runs, run => Assert.True(run.Start >= 50000 && run.Start + run.Length <= 50008));
        Assert.Equal(theme.SemanticColor("number"), runs.Single(run => run.Start == 50003).Color);
        var pending = Assert.Single(MacProductSourceModel.Foreground(projection, new(50000, 8), null, theme));
        Assert.Equal(new MacProductSourceModel.ForegroundRun(50000, 8, theme.Palette.EditorForeground), pending);
    }

    /// <summary>Offscreen semantic truth does not require offscreen native attribute mutation.</summary>
    [Fact]
    public void Offscreen_and_invalid_tokens_do_not_expand_work()
    {
        var theme = ThemePolicies.Get(ThemePolicies.LightId);
        var projection = new NativeTextProjection("0123456789", NativeLineEndingMode.Preserve);
        var semantics = new NativeSourceSemantics(new(1, 0), 1, 1, AnalysisCompleteness.Complete,
            new(0, 10), [new("string", new(0, 2)), new("number", new(8, 2)), new("error", new(-1, 100))], []);
        var run = Assert.Single(MacProductSourceModel.Foreground(projection, new(4, 2), semantics, theme));
        Assert.Equal(new MacProductSourceModel.ForegroundRun(4, 2, theme.Palette.EditorForeground), run);
        Assert.Empty(MacProductSourceModel.Foreground(projection, new(0, 0), semantics, theme));
    }

    /// <summary>Read-only salvage copy is unavailable to legacy, editable, canonical-retained or preview/table routes.</summary>
    [Fact]
    public void Unadmitted_copy_requires_all_source_only_guards()
    {
        for (var mask = 0; mask < 16; mask++)
            Assert.Equal(mask == 15, MacProductSourceModel.CanCopyUnadmittedSource(
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0));
    }

    /// <summary>Creates only an immutable portable installation, not an AppKit shell.</summary>
    private static NativeSourceInstallation Install(Document document) => new(document.Snapshot,
        new(1, document.Snapshot.Version), 1, new(document.Snapshot.GetText(), NativeLineEndingMode.Preserve), 0, 0);
}
