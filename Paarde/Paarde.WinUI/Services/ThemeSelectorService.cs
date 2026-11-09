using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;

namespace Paarde.WinUI.Services;

// specs/winui-frontend R3.1: AppTheme -> ElementTheme on the window's root element. Reads/writes the
// same setting as Paarde.Avalonia's ThemeSelectorService, so both apps share the chosen theme.
public class ThemeSelectorService : IThemeSelectorService
{
    public AppTheme Theme { get; set; } = AppTheme.Default;

    private readonly ILocalSettingsService _localSettingsService;

    public ThemeSelectorService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task InitializeAsync()
    {
        Theme = await LoadThemeFromSettingsAsync();
    }

    public async Task SetThemeAsync(AppTheme theme)
    {
        Theme = theme;

        await SetRequestedThemeAsync();
        await SaveThemeInSettingsAsync(Theme);
    }

    public Task SetRequestedThemeAsync()
    {
        if (App.MainWindow is { } window)
        {
            if (window.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = ToElementTheme(Theme);
            }

            // The caption buttons are drawn by the system, not XAML, so they need telling separately.
            window.AppWindow.TitleBar.PreferredTheme = ToTitleBarTheme(Theme);
        }

        return Task.CompletedTask;
    }

    private static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private static TitleBarTheme ToTitleBarTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => TitleBarTheme.Light,
        AppTheme.Dark => TitleBarTheme.Dark,
        _ => TitleBarTheme.UseDefaultAppMode,
    };

    private async Task<AppTheme> LoadThemeFromSettingsAsync()
    {
        var themeName = await _localSettingsService.ReadSettingAsync<string>(AppearanceSettingsKeys.RequestedTheme);

        return Enum.TryParse<AppTheme>(themeName, out var theme) ? theme : AppTheme.Default;
    }

    private async Task SaveThemeInSettingsAsync(AppTheme theme) =>
        await _localSettingsService.SaveSettingAsync(AppearanceSettingsKeys.RequestedTheme, theme.ToString());
}
