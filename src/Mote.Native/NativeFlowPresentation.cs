using Mote.Formats;

namespace Mote.Native;

/// <summary>Resolves block identity without erasing policy-owned inline semantics.</summary>
internal static class NativeFlowPresentation
{
    /// <summary>
    /// Finds the complete owner of a run; a run crossing a block boundary must
    /// not inherit either block's presentation. Ordered paragraph maps are bounded.
    /// </summary>
    internal static FlowParagraph? ContainingParagraph(FlowRenderProjection flow, FlowRun run)
    {
        foreach (var paragraph in flow.Paragraphs)
        {
            if (paragraph.DisplayRange.Start > run.DisplayRange.Start) break;
            if (run.DisplayRange.Start >= paragraph.DisplayRange.Start &&
                run.DisplayRange.End <= paragraph.DisplayRange.End) return paragraph;
        }
        return null;
    }

    /// <summary>
    /// Ordinary heading text inherits its block role. Explicit inline roles,
    /// including code and links, retain their own theme contract. This resolves
    /// appearance only: the original Flow and exact source origins are unchanged.
    /// </summary>
    internal static string Role(FlowRun run, FlowParagraph? paragraph) =>
        paragraph?.Kind == "heading" && run.Role is "text" or "paragraph"
            ? "heading" : run.Role;
}
