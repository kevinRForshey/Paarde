using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Helpers;
using Paarde.Presentation.Models;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ExplorerView : UserControl
{
    public ExplorerViewModel ViewModel
    {
        get;
    }

    private readonly ResizablePaneHelper _paneHelper;

    // Same ResizablePaneHelper as the tree pane, keyed to its own setting so
    // the two columns resize/persist independently. Not readonly/constructed eagerly like
    // _paneHelper above - Epic 26 needs an InterfaceMode read (async)
    // to pick the right first-run default before this can be constructed, so it's assigned later
    // from InitializeAssistantPanelHelperAsync instead; null until that completes.
    private ResizablePaneHelper? _assistantPanelHelper;

    // The secondary (split-right) TabView's column - starts
    // collapsed (no tabs in SecondaryGroupTabs yet) and is driven open/shut purely by
    // ExplorerViewModel.HasSecondaryGroupTabs, via OnViewModelPropertyChanged below, rather than
    // a user-facing toggle button like the Assistant panel's.
    private ResizablePaneHelper? _splitGroupHelper;

    // Tracks whether zen mode is currently active, plus whether the
    // tree/Assistant panes were already collapsed *before* zen mode was entered - so turning zen
    // mode back off only reopens panes zen mode itself closed, not ones the user had already
    // hidden deliberately.
    private bool _isZenModeActive;
    private bool _wasTreeCollapsedBeforeZenMode;
    private bool _wasAssistantPanelCollapsedBeforeZenMode;

    // Ctrl+K then Z - a chord, not a single Window.KeyBindings
    // gesture like the global Quick Open/Command Palette shortcuts, since this one is page-scoped
    // to ExplorerViewModel (AddTransient, not a singleton) rather than global. Local KeyDown/KeyUp
    // handlers + a timeout timer, since Avalonia has no built-in chord recognizer.
    private readonly DispatcherTimer _chordTimeoutTimer;
    private bool _isCtrlKArmed;

    public ExplorerView()
    {
        ViewModel = App.GetService<ExplorerViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        // UX pass: narrower default so the editor gets more width on first run - only affects
        // users with no saved width yet; TreeSplitter stays fully resizable either way.
        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerTreeWidth, defaultWidth: 220);
        Loaded += async (_, _) => await _paneHelper.RestoreAsync();
        TreeSplitter.DragCompleted += (_, _) => _paneHelper.OnSplitterDragCompleted();

        Loaded += async (_, _) => await InitializeAssistantPanelHelperAsync();
        AssistantPanelSplitter.DragCompleted += (_, _) => _assistantPanelHelper?.OnSplitterDragCompleted();

        Loaded += async (_, _) => await InitializeSplitGroupHelperAsync();
        SplitGroupSplitter.DragCompleted += (_, _) => _splitGroupHelper?.OnSplitterDragCompleted();

        ViewModel.ZenModeToggled += OnZenModeToggled;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        _chordTimeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _chordTimeoutTimer.Tick += (_, _) => DisarmCtrlK();

        KeyDown += OnViewKeyDown;
        KeyUp += OnViewKeyUp;
    }

    // FlowWizard mode has no Compose/Review page reachable
    // from here (they're separate navigation destinations in that mode, not a docked panel), so
    // the Assistant panel starts collapsed (the Assistant button still reopens it at 280) - only affects
    // users with no saved width yet, same as _paneHelper's own narrower default above.
    private async Task InitializeAssistantPanelHelperAsync()
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;

        // Narrower than the 360 this used to default to, so the
        // editor's * column visibly dominates a first-run window - only affects users with no
        // saved width yet, same as _paneHelper's own narrower default above.
        _assistantPanelHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[4], localSettingsService, WorkspaceLayoutSettingsKeys.ExplorerAssistantPanelWidth, defaultWidth: 280, startsCollapsed: interfaceMode == InterfaceMode.FlowWizard);
        await _assistantPanelHelper.RestoreAsync();
    }

    private void OnToggleAssistantPanelButtonClick(object? sender, RoutedEventArgs e)
    {
        _assistantPanelHelper?.ToggleCollapse();
        _assistantPanelHelper?.OnSplitterDragCompleted();
    }

    // The "x" on a TabViewItem only requests a close (so FluentAvalonia can't close a dirty tab
    // out from under CloseSelectedTabCommand's own unsaved-changes prompt) - select it first, then
    // run the same confirm/close path the "Close Tab" button already uses.
    private void OnTabViewTabCloseRequested(TabView? sender, TabViewTabCloseRequestedEventArgs e)
    {
        if (e.Item is OpenFileTabViewModel tab)
        {
            ViewModel.SelectedTab = tab;
            ViewModel.CloseSelectedTabCommand.Execute(null);
        }
    }

    // Starts collapsed - ExplorerViewModel.HasSecondaryGroupTabs is
    // false until a tab is split right, so there's nothing to restore a persisted width into yet.
    private async Task InitializeSplitGroupHelperAsync()
    {
        _splitGroupHelper = new ResizablePaneHelper(SplitGroupGrid.ColumnDefinitions[2], App.GetService<ILocalSettingsService>(), WorkspaceLayoutSettingsKeys.ExplorerSplitGroupWidth, defaultWidth: 320);
        await _splitGroupHelper.RestoreAsync();

        if (!ViewModel.HasSecondaryGroupTabs)
        {
            _splitGroupHelper.Collapse();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.HasSecondaryGroupTabs) && _splitGroupHelper is { } helper)
        {
            if (ViewModel.HasSecondaryGroupTabs)
            {
                helper.Restore();
            }
            else
            {
                helper.Collapse();
            }
        }
    }

    private void OnZenModeToggled()
    {
        if (_isZenModeActive)
        {
            _isZenModeActive = false;

            if (!_wasTreeCollapsedBeforeZenMode)
            {
                _paneHelper.Restore();
            }

            if (!_wasAssistantPanelCollapsedBeforeZenMode)
            {
                _assistantPanelHelper?.Restore();
            }
        }
        else
        {
            _isZenModeActive = true;
            _wasTreeCollapsedBeforeZenMode = _paneHelper.IsCollapsed;
            _wasAssistantPanelCollapsedBeforeZenMode = _assistantPanelHelper?.IsCollapsed ?? true;

            _paneHelper.Collapse();
            _assistantPanelHelper?.Collapse();
        }
    }

    private void DisarmCtrlK()
    {
        _isCtrlKArmed = false;
        _chordTimeoutTimer.Stop();
    }

    // Ctrl+K then Z toggles zen mode; Ctrl+Tab (held, repeated)
    // opens/advances the MRU switcher - releasing Ctrl (OnViewKeyUp below) commits whichever tab
    // is highlighted, same press-and-cycle convention as every mainstream editor's own Ctrl+Tab.
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel.IsMruSwitcherOpen)
            {
                ViewModel.AdvanceMruSwitcher();
            }
            else
            {
                ViewModel.OpenMruSwitcher();
            }

            e.Handled = true;
            return;
        }

        if (_isCtrlKArmed && e.Key == Key.Z)
        {
            DisarmCtrlK();
            ViewModel.ToggleZenModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.K && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _isCtrlKArmed = true;
            _chordTimeoutTimer.Stop();
            _chordTimeoutTimer.Start();
            e.Handled = true;
        }
    }

    private void OnViewKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && ViewModel.IsMruSwitcherOpen)
        {
            ViewModel.CommitMruSwitcher();
        }
    }
}
