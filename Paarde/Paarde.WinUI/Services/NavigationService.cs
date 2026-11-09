using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Contracts.ViewModels;
using Paarde.WinUI.Contracts.Services;
using Paarde.WinUI.Helpers;

namespace Paarde.WinUI.Services;

// Port of Paarde.Avalonia's NavigationService onto WinUI's Frame, which has the same
// Navigate/GoBack/BackStack/Navigating/Navigated shape - so the page-key contract, INavigationAware
// callbacks, IConfirmNavigationAway prompt and clearNavigation all behave identically.
public class NavigationService : IFrameNavigationService
{
    private readonly IPageService _pageService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<NavigationService> _logger;
    private object? _lastParameterUsed;
    private Frame? _frame;

    // Set while re-issuing a navigation the user already confirmed through the "unsaved changes"
    // dialog, so OnNavigating doesn't ask again for the exact same navigation it just approved.
    private bool _suppressUnsavedChangesCheck;

    public event NavigatedEventHandler? Navigated;

    public Frame? Frame
    {
        get => _frame;
        set
        {
            UnregisterFrameEvents();
            _frame = value;
            RegisterFrameEvents();
        }
    }

    [MemberNotNullWhen(true, nameof(Frame), nameof(_frame))]
    public bool CanGoBack => Frame != null && Frame.CanGoBack;

    public NavigationService(IPageService pageService, IDialogService dialogService, ILogger<NavigationService>? logger = null)
    {
        _pageService = pageService;
        _dialogService = dialogService;
        _logger = logger ?? NullLogger<NavigationService>.Instance;
    }

    private void RegisterFrameEvents()
    {
        if (_frame != null)
        {
            _frame.Navigated += OnNavigated;
            _frame.Navigating += OnNavigating;
        }
    }

    private void UnregisterFrameEvents()
    {
        if (_frame != null)
        {
            _frame.Navigated -= OnNavigated;
            _frame.Navigating -= OnNavigating;
        }
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        var vmBeforeNavigation = _frame.GetPageViewModel();
        _frame.GoBack();

        // Frame.GoBack() returns void, so the only way to tell whether it actually happened (as
        // opposed to being cancelled by OnNavigating below) is to check whether the page changed.
        var navigated = !ReferenceEquals(_frame.GetPageViewModel(), vmBeforeNavigation);
        if (navigated && vmBeforeNavigation is INavigationAware navigationAware)
        {
            navigationAware.OnNavigatedFrom();
        }

        return navigated;
    }

    public bool NavigateTo(string pageKey, object? parameter = null, bool clearNavigation = false) =>
        NavigateToPageType(_pageService.GetPageType(pageKey), parameter, clearNavigation);

    private bool NavigateToPageType(Type pageType, object? parameter, bool clearNavigation)
    {
        if (_frame != null && (_frame.Content?.GetType() != pageType || (parameter != null && !parameter.Equals(_lastParameterUsed))))
        {
            _frame.Tag = clearNavigation;
            var vmBeforeNavigation = _frame.GetPageViewModel();
            var navigated = _frame.Navigate(pageType, parameter);
            if (navigated)
            {
                _lastParameterUsed = parameter;
                if (vmBeforeNavigation is INavigationAware navigationAware)
                {
                    navigationAware.OnNavigatedFrom();
                }
            }

            return navigated;
        }

        return false;
    }

    // Fires before the frame's content changes, so it's the one place a navigation can still be
    // stopped. If the page being left has unsaved changes, cancel it immediately (Cancel has to be
    // set synchronously here) and ask the user via a dialog; if they choose to leave anyway,
    // re-issue the same navigation with the check suppressed.
    private void OnNavigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_suppressUnsavedChangesCheck || sender is not Frame frame)
        {
            return;
        }

        if (frame.GetPageViewModel() is not IConfirmNavigationAway { HasUnsavedChanges: true })
        {
            return;
        }

        e.Cancel = true;
        ConfirmThenRetryNavigation(e.NavigationMode, e.SourcePageType, e.Parameter);
    }

    // async void because it's raised from OnNavigating's synchronous event handler, so there's no
    // caller able to await it - wrapped in its own try/catch so an exception from the dialog await
    // doesn't unwind unobserved.
    private async void ConfirmThenRetryNavigation(NavigationMode mode, Type targetPageType, object? parameter)
    {
        try
        {
            var shouldLeave = await _dialogService.ConfirmAsync(
                "Discard unsaved edits?",
                "You have unsaved edits on this page. Leaving now will discard them.",
                "Leave without saving",
                "Stay");

            if (!shouldLeave)
            {
                return;
            }

            _suppressUnsavedChangesCheck = true;
            try
            {
                if (mode == NavigationMode.Back)
                {
                    GoBack();
                }
                else
                {
                    NavigateToPageType(targetPageType, parameter, clearNavigation: false);
                }
            }
            finally
            {
                _suppressUnsavedChangesCheck = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm and retry a navigation blocked by unsaved changes.");
        }
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (sender is Frame frame)
        {
            var clearNavigation = frame.Tag is true;
            if (clearNavigation)
            {
                frame.BackStack.Clear();
            }

            if (frame.GetPageViewModel() is INavigationAware navigationAware)
            {
                navigationAware.OnNavigatedTo(e.Parameter);
            }

            Navigated?.Invoke(sender, e);
        }
    }
}
