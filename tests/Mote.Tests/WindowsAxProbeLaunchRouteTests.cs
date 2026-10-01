using Mote.WindowsAxExternalProbe;

namespace Mote.Tests;

/// <summary>Prevents an external ordinary-product probe from silently testing a diagnostic route.</summary>
public sealed class WindowsAxProbeLaunchRouteTests
{
    /// <summary>Product launch passes only the fixture path while requiring source-fragment semantics.</summary>
    [Fact]
    public void Product_probe_has_no_presentation_flags()
    {
        var route = Assert.IsType<ProbeLaunchRoute>(ProbeLaunchRoute.Parse("--product-continuous"));
        Assert.Equal("product-continuous", route.Mode);
        Assert.True(route.UsesSourceFragment);
        Assert.Empty(route.PresentationArguments);
    }

    /// <summary>Historical baseline and fragment invocations retain their exact argument order.</summary>
    [Theory]
    [InlineData(null, "canvas-baseline", false)]
    [InlineData("--uia-fragment-experimental", "fragment-experimental", true)]
    public void Diagnostic_ab_routes_are_preserved(string? flag, string mode, bool fragment)
    {
        var route = Assert.IsType<ProbeLaunchRoute>(ProbeLaunchRoute.Parse(flag));
        Assert.Equal(mode, route.Mode);
        Assert.Equal(fragment, route.UsesSourceFragment);
        Assert.Equal(fragment
            ? new[] { "--canvas-experimental", "--uia-fragment-experimental" }
            : new[] { "--canvas-experimental" }, route.PresentationArguments);
    }

    /// <summary>An unrecognized client flag cannot become an accepted product/diagnostic route.</summary>
    [Theory]
    [InlineData("--legacy-page")]
    [InlineData("--canvas-experimental")]
    [InlineData("--product-continuous --uia-fragment-experimental")]
    public void Unknown_or_combined_client_modes_are_rejected(string flag) =>
        Assert.Null(ProbeLaunchRoute.Parse(flag));

    /// <summary>Foreign or unstable desktop focus cannot expose identity metadata in a product report.</summary>
    [Theory]
    [InlineData(true, 12, 12, true)]
    [InlineData(true, 12, 13, false)]
    [InlineData(false, 12, 12, false)]
    [InlineData(false, 12, 13, false)]
    [InlineData(true, 0, 0, false)]
    public void Focus_identity_requires_stable_target_ownership(
        bool stableForeground, int targetPid, int focusedPid, bool expected) =>
        Assert.Equal(expected, ProbeFocusScope.CanInspect(stableForeground, targetPid, focusedPid));
}
