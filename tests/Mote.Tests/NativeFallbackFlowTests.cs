using Mote.Engine;
using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Legacy semantic-to-Flow admission is bounded and never corrupts valid Unicode.</summary>
public sealed class NativeFallbackFlowTests
{
    /// <summary>A bounded column window reports omitted columns independently of validation.</summary>
    [Fact]
    public async Task Csv_fallback_flow_reports_omitted_ninth_column()
    {
        const string source = "a,b,c,d,e,f,g,h,i\n";
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full), default);
        Assert.True(result.Preview.Flow!.Truncated);
        Assert.Equal(AnalysisCompleteness.Complete, result.Analysis.Completeness);
        Assert.Contains(" │ …", result.Preview.Text, StringComparison.Ordinal);
    }
    /// <summary>Column clipping before a supplementary character is safe and truthfully omitted.</summary>
    [Fact]
    public async Task Csv_fallback_flow_clips_surrogate_pair_without_losing_semantic_completeness()
    {
        var source = new string('x', 22) + "😀tail\n";
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Csv));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Full), default);
        Assert.NotNull(result.Preview.Flow);
        Assert.True(result.Preview.Flow.Truncated);
        Assert.Equal(AnalysisCompleteness.Complete, result.Analysis.Completeness);
        Assert.DoesNotContain(result.Preview.Text, char.IsSurrogate);
    }

    /// <summary>Semantic scalar labels remain bounded before concatenation and preserve Unicode.</summary>
    [Fact]
    public async Task Json_fallback_flow_clips_scalar_without_invalid_utf16()
    {
        var source = "[\"" + new string('x', 118) + "😀tail\"]";
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.Json));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Visible), default);
        Assert.NotNull(result.Preview.Flow);
        Assert.True(result.Preview.Flow.Truncated);
        Assert.DoesNotContain(result.Preview.Text, char.IsSurrogate);
    }

    /// <summary>A user's literal omission-marker spelling is ordinary text, not rendering evidence.</summary>
    [Fact]
    public async Task Plain_fallback_marker_text_does_not_assert_truncation()
    {
        const string source = "… preview truncated …\n";
        using var document = new Document(source);
        using var driver = new NativeFormatSessionDriver(
            (IIncrementalDocumentPolicy)DocumentPolicies.ForKind(DocumentKind.PlainText));
        var result = await driver.AnalyzePresentationAsync(document.Snapshot,
            new AnalysisRequest(new TextSpan(0, source.Length), AnalysisScope.Visible), default);
        Assert.False(result.Preview.Flow!.Truncated);
        Assert.Equal(source, result.Preview.Text);
    }
}
