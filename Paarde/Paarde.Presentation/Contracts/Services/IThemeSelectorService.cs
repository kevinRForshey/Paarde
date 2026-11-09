using Paarde.Presentation.Models;

namespace Paarde.Presentation.Contracts.Services;

public interface IThemeSelectorService
{
    AppTheme Theme
    {
        get;
    }

    Task InitializeAsync();

    Task SetThemeAsync(AppTheme theme);

    Task SetRequestedThemeAsync();
}
