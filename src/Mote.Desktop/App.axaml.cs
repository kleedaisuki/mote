using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Mote.Themes;

namespace Mote.Desktop;

/// <summary>Owns one window and opens at most the explicitly supplied file.</summary>
public sealed partial class App : Application
{
    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        var prefersDark = PlatformSettings?.GetColorValues().ThemeVariant != PlatformThemeVariant.Light;
        var theme = ThemePolicies.Resolve(Program.Configuration.ThemeId, prefersDark);
        Program.Theme = theme;
        RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        InstallBrushes(theme.Palette);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            if (this.TryGetFeature<IActivatableLifetime>() is { } activation)
            {
                activation.Activated += (_, args) =>
                {
                    if (args is not FileActivatedEventArgs files) return;
                    var path = files.Files.FirstOrDefault()?.TryGetLocalPath();
                    if (path is not null) window.OpenFromActivation(path);
                };
            }
            if (Program.IsUiSmoke)
            {
                window.Opened += (_, _) => Program.ScheduleUiSmokeCompletion(window);
            }
            else if (desktop.Args is { Length: > 0 })
            {
                window.OpenAtStartup(desktop.Args[0]);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void InstallBrushes(ThemePalette palette)
    {
        Resources["MoteWindowBackground"] = Brush(palette.WindowBackground);
        Resources["MotePanelBackground"] = Brush(palette.PanelBackground);
        Resources["MoteEditorBackground"] = Brush(palette.EditorBackground);
        Resources["MoteEditorForeground"] = Brush(palette.EditorForeground);
        Resources["MotePreviewBackground"] = Brush(palette.PreviewBackground);
        Resources["MotePreviewForeground"] = Brush(palette.PreviewForeground);
        Resources["MoteMutedForeground"] = Brush(palette.MutedForeground);
        Resources["MoteBorder"] = Brush(palette.Border);
        Resources["MoteAccent"] = Brush(palette.Accent);
        Resources["MoteWarning"] = Brush(palette.Warning);
        Resources["MoteControlBackground"] = Brush(palette.ControlBackground);
        Resources["MoteControlForeground"] = Brush(palette.ControlForeground);
    }

    private static IBrush Brush(ThemeColor color) =>
        new SolidColorBrush(Color.Parse(color.ToHex()));
}
