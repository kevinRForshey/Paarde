using Paarde.Presentation.Models;
using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeThemeSelectorService : IThemeSelectorService
{
    public AppTheme Theme { get; private set; } = AppTheme.Default;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task SetThemeAsync(AppTheme theme)
    {
        Theme = theme;
        return Task.CompletedTask;
    }

    public Task SetRequestedThemeAsync() => Task.CompletedTask;
}
