namespace Mote.Native;

/// <summary>
/// The window-lifetime product presentation. A document may be replaced while
/// this value stays fixed; native preedit and accessibility identity are never
/// transferred between presentations inside a running window.
/// </summary>
internal enum EditorPresentationProfile
{
    /// <summary>Source-backed continuous canvas with one source accessibility document.</summary>
    Continuous,
    /// <summary>The established bounded native text page, retained as an explicit rollback.</summary>
    LegacyPage,
    /// <summary>Explicit full-native source product candidate; never selected by document size.</summary>
    NativeSource
}

/// <summary>
/// One parsed editor launch. Product profiles are closed; historical canvas
/// diagnostics remain a separate route so their UIA A/B behavior is preserved.
/// </summary>
internal abstract record NativeLaunchRoute(string? Path, bool Smoke)
{
    /// <summary>An ordinary editor launch using one immutable product profile.</summary>
    internal sealed record Product(EditorPresentationProfile Profile, string? Path, bool Smoke)
        : NativeLaunchRoute(Path, Smoke);

    /// <summary>The existing opt-in canvas/UIA diagnostic, not a product profile.</summary>
    internal sealed record CanvasDiagnostic(bool FragmentRoot, string? Path, bool Smoke)
        : NativeLaunchRoute(Path, Smoke);

    /// <summary>Whether this route creates the bounded native input island.</summary>
    internal bool UsesCanvas => this switch
    {
        Product { Profile: EditorPresentationProfile.Continuous } => true,
        Product { Profile: EditorPresentationProfile.LegacyPage or EditorPresentationProfile.NativeSource } => false,
        CanvasDiagnostic => true,
        _ => throw new InvalidOperationException("Unknown editor presentation.")
    };

    /// <summary>
    /// Windows product Continuous always uses the one-source UIA fragment root;
    /// only the historical diagnostic may deliberately compare it with the old tree.
    /// </summary>
    internal bool UsesWindowsSourceFragment => this switch
    {
        Product { Profile: EditorPresentationProfile.Continuous } => true,
        Product { Profile: EditorPresentationProfile.LegacyPage or EditorPresentationProfile.NativeSource } => false,
        CanvasDiagnostic diagnostic => diagnostic.FragmentRoot,
        _ => throw new InvalidOperationException("Unknown editor presentation.")
    };
}

/// <summary>Parses only ordinary editor flags; standalone diagnostic commands precede it.</summary>
internal static class NativeLaunchParser
{
    /// <summary>
    /// Produces one coherent route without opening files or querying the OS.
    /// Existing canvas diagnostic invocations retain their exact flag order.
    /// </summary>
    internal static bool TryParse(string[] args, out NativeLaunchRoute? route, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        route = null;
        error = null;
        var index = 0;
        var legacy = args.Length > 0 && args[0] == "--legacy-page";
        var source = args.Length > 0 && args[0] == "--native-source";
        var diagnostic = args.Length > 0 && args[0] == "--canvas-experimental";
        if (legacy || source || diagnostic) index++;
        var fragment = diagnostic && index < args.Length &&
            args[index] == "--uia-fragment-experimental";
        if (fragment) index++;

        // End-of-options preserves literal paths which resemble editor flags.
        var literalPath = index < args.Length && args[index] == "--";
        if (literalPath) index++;

        var remaining = args.AsSpan(index);
        if (!literalPath && remaining.Contains("--uia-fragment-experimental"))
        {
            error = "The UIA fragment diagnostic requires Windows canvas mode.";
            return false;
        }
        if (!literalPath && (remaining.Contains("--legacy-page") || remaining.Contains("--native-source") || remaining.Contains("--canvas-experimental")))
        {
            error = "Presentation modes cannot be combined.";
            return false;
        }
        if (remaining.Length > 1)
        {
            error = "mote opens one file per process; provide at most one path.";
            return false;
        }

        if (!literalPath && remaining.Length == 1 && remaining[0] != "--smoke-gui" &&
            remaining[0].StartsWith('-'))
        {
            error = "Unknown option. Use mote --help, or -- before a literal file path.";
            return false;
        }

        var smoke = !literalPath && remaining.Length == 1 && remaining[0] == "--smoke-gui";
        var path = remaining.Length == 1 && !smoke ? remaining[0] : null;
        route = diagnostic
            ? new NativeLaunchRoute.CanvasDiagnostic(fragment, path, smoke)
            : new NativeLaunchRoute.Product(legacy
                ? EditorPresentationProfile.LegacyPage
                : source ? EditorPresentationProfile.NativeSource : EditorPresentationProfile.Continuous, path, smoke);
        return true;
    }
}

/// <summary>Converts one validated route into the matching OS-native shell.</summary>
internal static class NativeShellFactory
{
    /// <summary>Preserves historical diagnostic A/B while product Canvas implies source UIA.</summary>
    internal static INativeEditorShell Create(NativeLaunchRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (OperatingSystem.IsWindows())
            return new Windows.WindowsEditorShell(route.UsesCanvas,
                route.UsesWindowsSourceFragment,
                route is NativeLaunchRoute.Product { Profile: EditorPresentationProfile.NativeSource });
        if (OperatingSystem.IsMacOS())
        {
            if (route.UsesWindowsSourceFragment && route is NativeLaunchRoute.CanvasDiagnostic)
                throw new PlatformNotSupportedException(
                    "The UIA fragment diagnostic requires Windows canvas mode.");
            return new Mac.MacEditorShell(route.UsesCanvas,
                route is NativeLaunchRoute.Product { Profile: EditorPresentationProfile.NativeSource });
        }
        throw new PlatformNotSupportedException(
            "mote's native desktop shell requires Windows or macOS.");
    }
}
