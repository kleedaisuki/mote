using Mote.Native;
using Mote.Native.Mac;
using Mote.Native.Windows;

namespace Mote.Tests;

/// <summary>
/// Keeps product presentation routing separate from historical canvas diagnostics.
/// Parsing is tested without creating a window or mutating user configuration.
/// </summary>
public sealed class NativePresentationProfileTests
{
    /// <summary>Unknown options refuse before any native shell or file I/O is created.</summary>
    [Theory]
    [InlineData("--unknown")]
    [InlineData("-x")]
    public void Unknown_options_are_not_file_paths(string option)
    {
        Assert.False(NativeLaunchParser.TryParse([option], out var route, out var error));
        Assert.Null(route);
        Assert.Contains("Unknown option", error);
    }

    /// <summary>End-of-options makes option-looking filenames explicit, including the smoke flag.</summary>
    [Theory]
    [InlineData("--native-source")]
    [InlineData("--smoke-gui")]
    public void Literal_option_looking_path_is_preserved(string path)
    {
        Assert.True(NativeLaunchParser.TryParse(["--", path], out var route, out var error), error);
        var product = Assert.IsType<NativeLaunchRoute.Product>(route);
        Assert.Equal(path, product.Path);
        Assert.False(product.Smoke);
    }

    /// <summary>The explicit full native product stays separate from default and diagnostic routing.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("note.txt", "note.txt", false)]
    [InlineData("--smoke-gui", null, true)]
    public void Native_source_flag_selects_only_full_native_product(string? argument, string? path, bool smoke)
    {
        var args = argument is null ? new[] { "--native-source" } : new[] { "--native-source", argument };
        Assert.True(NativeLaunchParser.TryParse(args, out var route, out var error), error);
        var product = Assert.IsType<NativeLaunchRoute.Product>(route);
        Assert.Equal(EditorPresentationProfile.NativeSource, product.Profile);
        Assert.Equal(path, product.Path);
        Assert.Equal(smoke, product.Smoke);
        Assert.False(product.UsesCanvas);
        Assert.False(product.UsesWindowsSourceFragment);
    }

    /// <summary>All conflicting profile combinations refuse rather than reinterpret a flag as a path.</summary>
    [Theory]
    [InlineData("--native-source --legacy-page")]
    [InlineData("--legacy-page --native-source")]
    [InlineData("--native-source --canvas-experimental")]
    [InlineData("--canvas-experimental --native-source")]
    [InlineData("--native-source --uia-fragment-experimental")]
    public void Native_source_conflicts_are_rejected(string command)
    {
        Assert.False(NativeLaunchParser.TryParse(command.Split(' '), out var route, out var error));
        Assert.Null(route);
        Assert.NotNull(error);
    }

    /// <summary>The explicit source factory creates no canvas and never starts a native event loop in this test.</summary>
    [Fact]
    public void Source_factory_selects_optional_source_capability()
    {
        var shell = NativeShellFactory.Create(new NativeLaunchRoute.Product(EditorPresentationProfile.NativeSource, null, false));
        Assert.True(Assert.IsAssignableFrom<INativeSourceShell>(shell).NativeSourceEnabled);
        Assert.False(Assert.IsAssignableFrom<INativeCanvasShell>(shell).CanvasEnabled);
    }

    /// <summary>Ordinary launch, file launch, and GUI smoke use the continuous product.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("note.txt", "note.txt", false)]
    [InlineData("--smoke-gui", null, true)]
    public void Ordinary_launch_uses_continuous_product(
        string? argument, string? expectedPath, bool expectedSmoke)
    {
        var args = argument is null ? Array.Empty<string>() : new[] { argument };

        var parsed = NativeLaunchParser.TryParse(args, out var route, out var error);

        Assert.True(parsed, error);
        var product = Assert.IsType<NativeLaunchRoute.Product>(route);
        Assert.Equal(EditorPresentationProfile.Continuous, product.Profile);
        Assert.Equal(expectedPath, product.Path);
        Assert.Equal(expectedSmoke, product.Smoke);
        Assert.True(product.UsesCanvas);
        Assert.True(product.UsesWindowsSourceFragment);
    }

    /// <summary>The explicit rollback retains page and non-fragment behavior.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("note.txt", "note.txt", false)]
    [InlineData("--smoke-gui", null, true)]
    public void Legacy_flag_selects_only_the_page_product(
        string? argument, string? expectedPath, bool expectedSmoke)
    {
        var args = argument is null
            ? new[] { "--legacy-page" }
            : new[] { "--legacy-page", argument };

        var parsed = NativeLaunchParser.TryParse(args, out var route, out var error);

        Assert.True(parsed, error);
        var product = Assert.IsType<NativeLaunchRoute.Product>(route);
        Assert.Equal(EditorPresentationProfile.LegacyPage, product.Profile);
        Assert.Equal(expectedPath, product.Path);
        Assert.Equal(expectedSmoke, product.Smoke);
        Assert.False(product.UsesCanvas);
        Assert.False(product.UsesWindowsSourceFragment);
    }

    /// <summary>Old canvas A/B flags stay diagnostic, not accidental product profiles.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Canvas_diagnostic_preserves_optional_fragment_root(
        bool fragmentRequested, bool expectedFragment)
    {
        var args = fragmentRequested
            ? new[] { "--canvas-experimental", "--uia-fragment-experimental", "note.txt" }
            : new[] { "--canvas-experimental", "note.txt" };

        var parsed = NativeLaunchParser.TryParse(args, out var route, out var error);

        Assert.True(parsed, error);
        var diagnostic = Assert.IsType<NativeLaunchRoute.CanvasDiagnostic>(route);
        Assert.Equal("note.txt", diagnostic.Path);
        Assert.False(diagnostic.Smoke);
        Assert.True(diagnostic.UsesCanvas);
        Assert.Equal(expectedFragment, diagnostic.FragmentRoot);
        Assert.Equal(expectedFragment, diagnostic.UsesWindowsSourceFragment);
    }

    /// <summary>Historical diagnostic GUI smoke still bypasses product presentation.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Canvas_diagnostic_smoke_keeps_its_ab_route(bool fragmentRequested)
    {
        var args = fragmentRequested
            ? new[] { "--canvas-experimental", "--uia-fragment-experimental", "--smoke-gui" }
            : new[] { "--canvas-experimental", "--smoke-gui" };

        Assert.True(NativeLaunchParser.TryParse(args, out var route, out var error), error);
        var diagnostic = Assert.IsType<NativeLaunchRoute.CanvasDiagnostic>(route);
        Assert.Null(diagnostic.Path);
        Assert.True(diagnostic.Smoke);
        Assert.Equal(fragmentRequested, diagnostic.UsesWindowsSourceFragment);
    }

    /// <summary>Conflicting presentation flags fail rather than becoming a filename.</summary>
    [Theory]
    [InlineData("--legacy-page --canvas-experimental")]
    [InlineData("--canvas-experimental --legacy-page")]
    [InlineData("--uia-fragment-experimental")]
    [InlineData("--legacy-page --uia-fragment-experimental")]
    [InlineData("--canvas-experimental --uia-fragment-experimental --legacy-page")]
    [InlineData("one.txt two.txt")]
    public void Conflicting_or_multiple_launch_arguments_are_rejected(string commandLine)
    {
        var args = commandLine.Split(' ');
        var parsed = NativeLaunchParser.TryParse(args, out var route, out var error);

        Assert.False(parsed);
        Assert.Null(route);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>The factory respects product Canvas and the page rollback on the current OS.</summary>
    [Fact]
    public void Factory_maps_product_profiles_to_the_current_os_shell()
    {
        var continuous = NativeShellFactory.Create(
            new NativeLaunchRoute.Product(EditorPresentationProfile.Continuous, null, false));
        var legacy = NativeShellFactory.Create(
            new NativeLaunchRoute.Product(EditorPresentationProfile.LegacyPage, null, false));

        Assert.True(Assert.IsAssignableFrom<INativeCanvasShell>(continuous).CanvasEnabled);
        Assert.False(Assert.IsAssignableFrom<INativeCanvasShell>(legacy).CanvasEnabled);
        if (OperatingSystem.IsWindows())
        {
            Assert.IsType<WindowsEditorShell>(continuous);
            Assert.IsType<WindowsEditorShell>(legacy);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<MacEditorShell>(continuous);
            Assert.IsType<MacEditorShell>(legacy);
        }
        else
        {
            Assert.Fail("The native shell factory is intended for Windows and macOS only.");
        }
    }

    /// <summary>macOS must not accept Windows-only UIA fragment diagnostics.</summary>
    [Fact]
    public void Fragment_diagnostic_factory_is_windows_only()
    {
        var route = new NativeLaunchRoute.CanvasDiagnostic(true, null, false);

        if (OperatingSystem.IsMacOS())
            Assert.Throws<PlatformNotSupportedException>(() => NativeShellFactory.Create(route));
        else if (OperatingSystem.IsWindows())
            Assert.IsType<WindowsEditorShell>(NativeShellFactory.Create(route));
    }
}
