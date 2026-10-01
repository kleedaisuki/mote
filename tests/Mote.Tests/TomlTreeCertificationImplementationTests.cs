using Mote.Formats;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Mote.Tests;

/// <summary>Implementation-focused rejection checks for absent structure and cancellation.</summary>
public sealed class TomlTreeCertificationImplementationTests
{
    /// <summary>Missing public mutable syntax properties cannot become a validity certificate.</summary>
    [Theory]
    [InlineData("equal")]
    [InlineData("key")]
    [InlineData("value")]
    [InlineData("integer-token")]
    [InlineData("dot")]
    [InlineData("dot-key")]
    public void Missing_assignment_structure_is_not_certified(string member)
    {
        var syntax = SyntaxParser.Parse("a.b=1\n", validate: false);
        var pair = syntax.KeyValues.First();
        switch (member)
        {
            case "equal": pair.EqualToken = null; break;
            case "key": pair.Key = null; break;
            case "value": pair.Value = null; break;
            case "integer-token": ((IntegerValueSyntax)pair.Value!).Token = null; break;
            case "dot": pair.Key!.DotKeys.First().Dot = null; break;
            case "dot-key": pair.Key!.DotKeys.First().Key = null; break;
        }
        Assert.False(TomlTreeCertification.TryCertify(syntax));
    }

    /// <summary>Cancellation propagates rather than publishing an unknown/valid result.</summary>
    [Fact]
    public void Pre_cancelled_valid_or_invalid_tree_throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        foreach (var source in new[] { "a=1\n", "a=[\n" })
            Assert.Throws<OperationCanceledException>(() => TomlTreeCertification.TryCertify(
                SyntaxParser.Parse(source, validate: false), cancellation.Token));
    }

    /// <summary>Fresh local scopes permit equal keys in separate array elements and nested values.</summary>
    [Theory]
    [InlineData("a=[{x=1},{x=2}]\n", true)]
    [InlineData("a={x={k=1},y={k=2}}\n", true)]
    [InlineData("a={x.y=1,x.z=2}\n", true)]
    [InlineData("a={x.y=1,x=2}\n", false)]
    [InlineData("a=[{x=1,x=2}]\n", false)]
    [InlineData("[[a]]\n[[a]]\n[[a.b]]\n[[a]]\nx={a=1}\na=1\nb=2\n", true)]
    public void Nested_namespace_certification(string source, bool expected)
        => Assert.Equal(expected, TomlTreeCertification.TryCertify(SyntaxParser.Parse(source, validate: false)));
}
