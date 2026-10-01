namespace Mote.WindowsAxExternalProbe;

/// <summary>Separates ordinary product launch from the preserved diagnostic A/B routes.</summary>
internal sealed record ProbeLaunchRoute(string Mode, bool UsesSourceFragment, string[] PresentationArguments)
{
    /// <summary>
    /// Parses the client's optional mode flag. Product mode intentionally adds no
    /// presentation flags to mote, so a diagnostic route cannot mask a routing bug.
    /// </summary>
    internal static ProbeLaunchRoute? Parse(string? flag) => flag switch
    {
        null => new("canvas-baseline", false, ["--canvas-experimental"]),
        "--uia-fragment-experimental" => new("fragment-experimental", true,
            ["--canvas-experimental", "--uia-fragment-experimental"]),
        "--product-continuous" => new("product-continuous", true, []),
        _ => null
    };
}

/// <summary>Privacy boundary for desktop-global focus, independent of provider correctness.</summary>
internal static class ProbeFocusScope
{
    /// <summary>Only a target-owned element under stable target foreground may reveal identity metadata.</summary>
    internal static bool CanInspect(bool stableTargetForeground, int targetPid, int focusedPid) =>
        stableTargetForeground && targetPid > 0 && focusedPid == targetPid;
}
