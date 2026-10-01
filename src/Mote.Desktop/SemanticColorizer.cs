using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Mote.Formats;
using Mote.Themes;

namespace Mote.Desktop;

/// <summary>
/// Colors only visible editor lines from immutable, version-matched semantic tokens.
/// Source offsets are translated once when a paged document is projected into the editor.
/// </summary>
internal sealed class SemanticColorizer : DocumentColorizingTransformer
{
    private static readonly string[] ColorKinds =
    [
        "key", "heading", "string", "number", "boolean", "null", "keyword",
        "comment", "heading-marker", "list-marker", "sequence-marker", "fence",
        "fenced-code", "code", "emphasis", "link", "error", "invalid"
    ];
    private readonly Dictionary<string, IBrush> _brushes;
    private SemanticToken[] _tokens = [];
    private int[] _maxEnds = [];

    /// <summary>Precomputes brushes from a compile-time registered theme policy.</summary>
    public SemanticColorizer(IThemePolicy theme) =>
        _brushes = ColorKinds.ToDictionary(kind => kind,
            kind => (IBrush)new SolidColorBrush(Color.Parse(theme.SemanticColor(kind).ToHex())));

    /// <summary>Installs a sorted token snapshot whose spans are editor-local UTF-16 offsets.</summary>
    public void SetTokens(IReadOnlyList<SemanticToken> tokens, int sourceStart, int viewLength)
    {
        var end = sourceStart + viewLength;
        _tokens = tokens
            .Where(token => token.Span.End > sourceStart && token.Span.Start < end)
            .Select(token => new SemanticToken(token.Kind,
                new TextSpan(Math.Max(token.Span.Start, sourceStart) - sourceStart,
                    Math.Min(token.Span.End, end) - Math.Max(token.Span.Start, sourceStart))))
            .Where(token => token.Span.Length > 0)
            .OrderBy(token => token.Span.Start)
            .ToArray();
        _maxEnds = new int[_tokens.Length];
        var maxEnd = 0;
        for (var i = 0; i < _tokens.Length; i++)
        {
            maxEnd = Math.Max(maxEnd, _tokens[i].Span.End);
            _maxEnds[i] = maxEnd;
        }
    }

    /// <inheritdoc />
    protected override void ColorizeLine(DocumentLine line)
    {
        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;
        var tokens = _tokens;
        var index = LowerBound(_maxEnds, lineStart);
        while (index < tokens.Length && tokens[index].Span.Start < lineEnd)
        {
            var token = tokens[index++];
            var start = Math.Max(lineStart, token.Span.Start);
            var end = Math.Min(lineEnd, token.Span.End);
            if (start >= end) continue;
            var brush = ForKind(token.Kind);
            if (brush is null) continue;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private static int LowerBound(int[] maxEnds, int offset)
    {
        var left = 0;
        var right = maxEnds.Length;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (maxEnds[middle] <= offset) left = middle + 1;
            else right = middle;
        }
        return left;
    }

    private IBrush? ForKind(string kind) =>
        _brushes.TryGetValue(kind, out var brush) ? brush : null;
}
