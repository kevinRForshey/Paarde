using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Paarde.Presentation.Strings;
using Paarde.WinUI.Views;

using Windows.Graphics;

namespace Paarde.WinUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = "AppDisplayName".Resolve();
        AppTitleBar.Title = Title;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Same starting size as Paarde.Avalonia's MainWindow (AppWindow sizes are physical pixels).
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1200 * scale), (int)(800 * scale)));
    }

    public void SetShell(ShellView shell) => ShellHost.Content = shell;

    // Off by default - called once at startup
    // with whatever was saved, and again live from SettingsViewModel when toggled. Same fallback order
    // as Paarde.Avalonia: Acrylic, then Mica, then a solid background. Acrylic first because it
    // blurs the windows behind the app; Mica only tints from the wallpaper, which in Dark theme
    // was a 1-2 RGB-value difference - the toggle looked like it did nothing.
    public void ApplyTransparency(bool enabled)
    {
        SystemBackdrop = !enabled ? null
            : DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop()
            : MicaController.IsSupported() ? new MicaBackdrop()
            : null;

        SolidBackground.Visibility = SystemBackdrop is null ? Visibility.Visible : Visibility.Collapsed;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
