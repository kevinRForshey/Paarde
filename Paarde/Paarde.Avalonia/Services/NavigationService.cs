using System.Diagnostics.CodeAnalysis;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Presentation.Contracts.ViewModels;
using Paarde.Avalonia.Helpers;

using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Navigation;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Services;

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

    // Fires before the frame's content changes - unlike OnNavigatedTo/OnNavigatedFrom, which both
    // only ever run after a navigation has already happened - so it's the one place a navigation
    // can still be stopped. If the page being left has unsaved changes, cancel it immediately
    // (NavigatingCancelEventArgs.Cancel has to be set synchronously here) and ask the user via a
    // dialog; if they choose to leave anyway, re-issue the same navigation with the check
    // suppressed.
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

    // Async void because it's raised from OnNavigating's event handler (itself required to
    // be synchronous by Frame.Navigating's delegate signature), so there's no caller able to await
    // it - wrapped in its own try/catch (rather than just the inner try/finally below) so an
    // exception from the dialog await itself doesn't unwind unobserved past this method too.
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
