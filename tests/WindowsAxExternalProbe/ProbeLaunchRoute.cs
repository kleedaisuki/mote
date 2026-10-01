namespace Mote.WindowsAxExternalProbe;

/// <summary>Separates the retained Continuous product from the preserved diagnostic A/B routes.</summary>
internal sealed record ProbeLaunchRoute(string Mode, bool UsesSourceFragment, string[] PresentationArguments)
{
    /// <summary>
    /// Parses the client's optional mode flag. Continuous mode explicitly selects
    /// the retained product after ordinary launch moved to the full native source.
    /// Historical evidence remains about that route, not the new release default.
    /// </summary>
    internal static ProbeLaunchRoute? Parse(string? flag) => flag switch
    {
        null => new("canvas-baseline", false, ["--canvas-experimental"]),
        "--uia-fragment-experimental" => new("fragment-experimental", true,
            ["--canvas-experimental", "--uia-fragment-experimental"]),
        "--product-continuous" => new("product-continuous", true, ["--continuous"]),
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
