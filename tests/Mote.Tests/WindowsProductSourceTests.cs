using Mote.Engine;
using Mote.Formats;
using Mote.Native.Windows;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Portable Windows source decoration contracts; never creates a native control.</summary>
public sealed class WindowsProductSourceTests
{
    /// <summary>Foreground precedence is publication order, followed by exact adjacent coalescing.</summary>
    [Fact]
    public void OverlapsCoalesceAndStayInsideInterest()
    {
        var runs = WindowsSourceForegroundPlan.Build("abcdefghij", new TextSpan(2, 5), 0,
            [new(0, 10, 1), new(4, 6, 2), new(5, 6, 1)]);
        Assert.Equal([new WindowsSourceForegroundPlan.Run(2, 4, 1), new(4, 5, 2), new(5, 7, 1)], runs);
    }

    /// <summary>Empty interests issue no native attribute work.</summary>
    [Fact]
    public void EmptyProducesNoRuns() => Assert.Empty(WindowsSourceForegroundPlan.Build("abc", new TextSpan(1, 0), 7, []));

    /// <summary>Neutral presentation revokes stale token attributes throughout the bounded interest.</summary>
    [Fact]
    public void NeutralUsesOneVisibleRun() => Assert.Equal(
        [new WindowsSourceForegroundPlan.Run(2, 6, 7)],
        WindowsSourceForegroundPlan.Build("abcdefghij", new TextSpan(2, 4), 7, []));

    /// <summary>Viewport clipping never expands to an entire long paragraph.</summary>
    [Fact]
    public void LongLineRemainsBounded()
    {
        var display = new string('x', 100000);
        var runs = WindowsSourceForegroundPlan.Build(display, new TextSpan(23000, 8192), 7, [new(0, display.Length, 2)]);
        Assert.Equal([new WindowsSourceForegroundPlan.Run(23000, 31192, 2)], runs);
    }

    /// <summary>Interest endpoints inside CRLF/scalars trim inward instead of publishing trailing halves.</summary>
    [Theory]
    [InlineData("a😀b", 2, 2, 3, 1)]
    [InlineData("a😀b", 0, 2, 0, 1)]
    [InlineData("a\r\nb", 2, 2, 3, 1)]
    [InlineData("a\r\nb", 0, 2, 0, 1)]
    public void PartialUnitsAreTrimmed(string text, int start, int length, int expectedStart, int expectedLength) =>
        Assert.Equal(new TextSpan(expectedStart, expectedLength), WindowsSourceForegroundPlan.Trim(text, new TextSpan(start, length)));

    /// <summary>Malformed trailing-half token decoration never creates a partial native scalar range.</summary>
    [Fact]
    public void ColorsDoNotSplitScalar()
    {
        var runs = WindowsSourceForegroundPlan.Build("a😀b", new TextSpan(0, 4), 0, [new(2, 3, 1)]);
        Assert.Equal([new WindowsSourceForegroundPlan.Run(0, 4, 0)], runs);
    }

    /// <summary>A rejected callback, unavailable input or unrelated binding cannot authorize canonical commands.</summary>
    [Theory]
    [InlineData("accepted", true)]
    [InlineData("missing", false)]
    [InlineData("old-version", false)]
    [InlineData("other-document", false)]
    [InlineData("other-nonce", false)]
    [InlineData("wrong-projection", false)]
    [InlineData("wrong-installed", false)]
    [InlineData("uncertified", false)]
    [InlineData("readonly", false)]
    public void SettlementRequiresExactAcknowledgement(string reason, bool expected)
    {
        using var doc = new Document("a");
        var original = new NativeSourceInstallation(doc.Snapshot, new(7, 0), 31,
            new NativeTextProjection("a", NativeLineEndingMode.CrLf), 0, 0);
        doc.Apply(new(1, 0, "b"));
        NativeSourceInstallation? accepted = new(doc.Snapshot,
            new(reason == "other-document" ? 8 : 7, reason == "old-version" ? 0 : doc.Snapshot.Version),
            reason == "other-nonce" ? 32 : 31,
            new NativeTextProjection(reason == "wrong-projection" ? "a" : "ab", NativeLineEndingMode.CrLf), 2, 2);
        if (reason == "missing") accepted = null;
        Assert.Equal(expected, WindowsSourceSettlement.Acknowledged(original, accepted, "ab",
            reason == "wrong-installed" ? "a" : "ab", reason != "uncertified", reason == "readonly"));
    }
}
