using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;
using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Contracts.Services;

// The FluentAvalonia-specific side of INavigationService -
// only the shell (ShellView/ShellViewModel) needs to hand over the Frame and react to Navigated.
// DI maps this and INavigationService to the same NavigationService singleton.
public interface IFrameNavigationService : INavigationService
{
    event NavigatedEventHandler Navigated;

    Frame? Frame
    {
        get; set;
    }
}
