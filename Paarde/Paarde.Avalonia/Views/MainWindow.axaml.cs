using Avalonia.Controls;
using Avalonia.Media;

using Paarde.Avalonia.Strings;

namespace Paarde.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = "AppDisplayName".Resolve();
    }

    // specs/fluent-design-pass R1: off by default (see AppearanceSettingsKeys) - called once at
    // startup with whatever was saved, and again live from SettingsViewModel when toggled.
    // Background only needs to go transparent when the backdrop hint is actually in play; without
    // it, a transparent Background would just show the desktop through an unfilled window, so
    // disabling clears the local value and lets the theme's own Window background style apply.
    public void ApplyTransparency(bool enabled)
    {
        if (enabled)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
            Background = Brushes.Transparent;
        }
        else
        {
            TransparencyLevelHint = [WindowTransparencyLevel.None];
            ClearValue(BackgroundProperty);
        }
    }
}
