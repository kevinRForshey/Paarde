using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeNavigationService : INavigationService
{
    public bool CanGoBack => false;

    public int NavigateToCallCount { get; private set; }

    public string? LastPageKey { get; private set; }

    public object? LastParameter { get; private set; }

    public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false)
    {
        NavigateToCallCount++;
        LastPageKey = pageKey;
        LastParameter = parameter;
        return true;
    }

    public bool GoBack() => false;
}
