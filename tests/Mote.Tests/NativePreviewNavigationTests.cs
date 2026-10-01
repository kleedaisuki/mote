using Mote.Formats;
using Mote.Native;

namespace Mote.Tests;

/// <summary>Pure semantic-preview mapping and unsupported-region contracts.</summary>
public sealed class NativePreviewNavigationTests
{
    /// <summary>Plain lines retain their own absolute source starts across mixed newlines.</summary>
    [Fact]
    public void Plain_preview_lines_map_to_distinct_source_offsets()
    {
        const string source = "first\r\nsecond\nthird";
        var analysis = DocumentPolicies.ForKind(DocumentKind.PlainText).Analyze(source);
        var preview = NativePreviewBuilder.Build(analysis, DocumentKind.PlainText, 0, true);
        var view = View(preview);

        Assert.Equal(0, NativePreviewNavigation.SourceStart(view, 1, source.Length));
        Assert.Equal(7, NativePreviewNavigation.SourceStart(view,
            preview.Text.IndexOf("second", StringComparison.Ordinal), source.Length));
        Assert.Equal(14, NativePreviewNavigation.SourceStart(view,
            preview.Text.IndexOf("third", StringComparison.Ordinal), source.Length));
        Assert.Null(NativePreviewNavigation.SourceStart(view,
            preview.Text.IndexOf('\n'), source.Length));
    }

    /// <summary>CSV activation identifies a cell, not a containing row or its column padding.</summary>
    [Fact]
    public void Csv_preview_maps_cells_but_not_structural_delimiters()
    {
        const string source = "alpha,beta\nleft,right\n";
        var analysis = DocumentPolicies.ForKind(DocumentKind.Csv).Analyze(source);
        var preview = NativePreviewBuilder.Build(analysis, DocumentKind.Csv, 0, true);
        var view = View(preview);
        var beta = preview.Text.IndexOf("beta", StringComparison.Ordinal);
        Assert.True(beta >= 0);

        var target = NativePreviewNavigation.SourceStart(view, beta, source.Length);
        Assert.True(target == source.IndexOf("beta", StringComparison.Ordinal),
            $"preview={preview.Text}; spans={string.Join(';', preview.Spans)}; target={target}");
        Assert.Null(NativePreviewNavigation.SourceStart(view, 0, source.Length));
        Assert.Null(NativePreviewNavigation.SourceStart(view, -1, source.Length));
        Assert.Null(NativePreviewNavigation.SourceStart(view, preview.Text.Length, source.Length));
    }

    /// <summary>Incomplete-context banners and malformed ranges never create destinations.</summary>
    [Fact]
    public void Preview_navigation_rejects_decorations_and_out_of_bounds_mapping()
    {
        const string source = "# Heading\n";
        var analysis = DocumentPolicies.ForKind(DocumentKind.Markdown).Analyze(source);
        var preview = NativePreviewBuilder.Build(analysis, DocumentKind.Markdown, 0, false);
        var view = View(preview);
        Assert.Null(NativePreviewNavigation.SourceStart(view, 0, source.Length));
        Assert.Equal(0, NativePreviewNavigation.SourceStart(view,
            preview.Text.IndexOf("Heading", StringComparison.Ordinal), source.Length));

        var invalid = view with
        {
            PreviewSpans = [new NativePreviewSpan(0, 1, "heading",
                new TextSpan(source.Length + 1, 1))]
        };
        Assert.Null(NativePreviewNavigation.SourceStart(invalid, 0, source.Length));
    }

    /// <summary>The synthetic truncation suffix cannot inherit a later CSV cell's source map.</summary>
    [Fact]
    public void Csv_truncation_marker_has_no_source_destination()
    {
        var row = string.Join(',', Enumerable.Repeat("abcdefghijklmnopqrstuvwx", 8)) + "\n";
        var source = string.Concat(Enumerable.Repeat(row, 120));
        var analysis = DocumentPolicies.ForKind(DocumentKind.Csv).Analyze(source);
        var preview = NativePreviewBuilder.Build(analysis, DocumentKind.Csv, 0, true);
        var marker = preview.Text.IndexOf("… preview truncated …", StringComparison.Ordinal);
        Assert.True(marker >= 0);
        Assert.Null(NativePreviewNavigation.SourceStart(View(preview), marker, source.Length));
    }

    private static NativeAnalysisView View(NativePreview preview) =>
        new([], "", preview.Text, "", new NativeDocumentStamp(1, 0), preview.Spans);
}
