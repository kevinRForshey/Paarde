using Avalonia;
using Avalonia.Styling;

using Paarde.Presentation.Models;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;

namespace Paarde.Avalonia.Services;

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
        await Task.CompletedTask;
    }

    public async Task SetThemeAsync(AppTheme theme)
    {
        Theme = theme;

        await SetRequestedThemeAsync();
        await SaveThemeInSettingsAsync(Theme);
    }

    public async Task SetRequestedThemeAsync()
    {
        if (Application.Current != null)
        {
            Application.Current.RequestedThemeVariant = ToThemeVariant(Theme);
        }

        await Task.CompletedTask;
    }

    private static ThemeVariant ToThemeVariant(AppTheme theme) => theme switch
    {
        AppTheme.Light => ThemeVariant.Light,
        AppTheme.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private async Task<AppTheme> LoadThemeFromSettingsAsync()
    {
        var themeName = await _localSettingsService.ReadSettingAsync<string>(AppearanceSettingsKeys.RequestedTheme);

        return themeName switch
        {
            "Light" => AppTheme.Light,
            "Dark" => AppTheme.Dark,
            _ => AppTheme.Default,
        };
    }

    // Persisted as the member name ("Default"/"Light"/"Dark") - the same strings this saved back
    // when it worked in ThemeVariant terms, so existing settings files keep loading unchanged.
    private async Task SaveThemeInSettingsAsync(AppTheme theme) =>
        await _localSettingsService.SaveSettingAsync(AppearanceSettingsKeys.RequestedTheme, theme.ToString());
}
