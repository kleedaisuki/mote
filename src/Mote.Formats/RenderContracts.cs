using System.Collections.ObjectModel;
using Mote.Engine;

namespace Mote.Formats;

/// <summary>Compositional inline presentation, interpreted by the native theme.</summary>
[Flags]
public enum FlowInlineStyle { None = 0, Strong = 1, Emphasis = 2, Code = 4, Link = 8 }

/// <summary>Whether source navigation identifies an item or an unchanged literal slice.</summary>
public enum RenderOriginPrecision { Item, ExactText }

/// <summary>A disjoint display run with absolute UTF-16 source provenance.</summary>
public readonly record struct FlowRun(TextSpan DisplayRange, TextSpan SourceRange, string Role,
    FlowInlineStyle Style, RenderOriginPrecision Precision, bool Navigable = true);

/// <summary>A display paragraph with semantic indentation and an optional logical list marker.</summary>
public readonly record struct FlowParagraph(TextSpan DisplayRange, TextSpan SourceRange, string Kind,
    int Level = 0, int Depth = 0, string? Marker = null);

/// <summary>An immutable bounded presentation; semantic completeness is independent of omission.</summary>
public sealed class FlowRenderProjection
{
    /// <summary>Maximum UTF-16 display size, including synthetic notices.</summary>
    public const int MaxTextLength = 16 * 1024;
    /// <summary>Maximum paragraph count, including synthetic notices.</summary>
    public const int MaxParagraphs = 120;
    /// <summary>Maximum disjoint inline run count, including synthetic notices.</summary>
    public const int MaxRuns = 4096;

    /// <summary>Copies and validates a projection without retaining parser or engine objects.</summary>
    public FlowRenderProjection(long version, string text, IReadOnlyList<FlowRun> runs,
        IReadOnlyList<FlowParagraph> paragraphs, bool truncated, AnalysisCompleteness completeness)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(paragraphs);
        if (text.Length > MaxTextLength || runs.Count > MaxRuns || paragraphs.Count > MaxParagraphs)
            throw new ArgumentException("Flow projection exceeds its resource limits.");
        if (!Enum.IsDefined(completeness)) throw new ArgumentOutOfRangeException(nameof(completeness));
        var runCopy = runs.ToArray();
        var paragraphCopy = paragraphs.ToArray();
        ValidateUnicode(text);
        var previous = 0;
        foreach (var run in runCopy)
        {
            ValidateRange(run.DisplayRange, text.Length);
            ValidateRange(run.SourceRange, int.MaxValue);
            if (run.DisplayRange.Start < previous || string.IsNullOrEmpty(run.Role) || run.Role.Length > 64 ||
                !Enum.IsDefined(run.Precision) || ((int)run.Style & ~15) != 0 ||
                (run.Precision == RenderOriginPrecision.ExactText &&
                    run.DisplayRange.Length != run.SourceRange.Length))
                throw new ArgumentException("Invalid or overlapping Flow run.", nameof(runs));
            ValidateBoundary(text, run.DisplayRange.Start);
            ValidateBoundary(text, run.DisplayRange.End);
            previous = run.DisplayRange.End;
        }
        previous = 0;
        foreach (var paragraph in paragraphCopy)
        {
            ValidateRange(paragraph.DisplayRange, text.Length);
            ValidateRange(paragraph.SourceRange, int.MaxValue);
            if (paragraph.DisplayRange.Start < previous || string.IsNullOrEmpty(paragraph.Kind) || paragraph.Kind.Length > 64 ||
                paragraph.Level is < 0 or > 6 || paragraph.Depth is < 0 or > 64 || paragraph.Marker?.Length > 32)
                throw new ArgumentException("Invalid or overlapping Flow paragraph.", nameof(paragraphs));
            ValidateBoundary(text, paragraph.DisplayRange.Start);
            ValidateBoundary(text, paragraph.DisplayRange.End);
            previous = paragraph.DisplayRange.End;
        }
        Version = version;
        Text = text;
        Runs = new ReadOnlyCollection<FlowRun>(runCopy);
        Paragraphs = new ReadOnlyCollection<FlowParagraph>(paragraphCopy);
        Truncated = truncated;
        Completeness = completeness;
    }

    /// <summary>Snapshot version to which every source origin belongs.</summary>
    public long Version { get; }
    /// <summary>Rendered display text, never the canonical source buffer.</summary>
    public string Text { get; }
    /// <summary>Copied, ordered, nonoverlapping display runs.</summary>
    public IReadOnlyList<FlowRun> Runs { get; }
    /// <summary>Copied, ordered, nonoverlapping display paragraphs.</summary>
    public IReadOnlyList<FlowParagraph> Paragraphs { get; }
    /// <summary>True when requested presentation content was omitted.</summary>
    public bool Truncated { get; }
    /// <summary>Semantic truth inherited from the corresponding analysis.</summary>
    public AnalysisCompleteness Completeness { get; }

    private static void ValidateRange(TextSpan range, int limit)
    {
        if (range.Start < 0 || range.Length < 0 || range.Start > limit - range.Length)
            throw new ArgumentException("Invalid UTF-16 range.");
    }

    private static void ValidateBoundary(string text, int offset)
    {
        if (offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) &&
            char.IsLowSurrogate(text[offset])) throw new ArgumentException("Range splits a surrogate pair.");
    }

    private static void ValidateUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i]))
                    throw new ArgumentException("Display text contains an unpaired surrogate.");
            }
            else if (char.IsLowSurrogate(text[i])) throw new ArgumentException("Display text contains an unpaired surrogate.");
        }
    }
}

/// <summary>Optional format-owned rendering on the serialized analysis session.</summary>
public interface IRenderFormatSession : IFormatSession
{
    /// <summary>Projects the matching analysis without committing or changing semantic cache state.</summary>
    /// <remarks>Call Analyze and Render without an intervening edit or concurrent session call.</remarks>
    FlowRenderProjection Render(TextSnapshot snapshot, DocumentAnalysis analysis, AnalysisRequest request,
        CancellationToken cancellationToken = default);
}
