using System.ComponentModel;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Helpers;

using Windows.UI.Core;

using VirtualKey = Windows.System.VirtualKey;

namespace Paarde.WinUI.Views;

public sealed partial class ExplorerView : Page
{
    private const double MruSwitcherWidth = 360;

    private readonly ResizablePaneHelper _paneHelper;

    // The first-run default depends on the interface mode, an
    // async read, so this is created on Loaded; null until then.
    private ResizablePaneHelper? _assistantPanelHelper;

    // The split group's column, opened and shut by
    // HasSecondaryGroupTabs.
    private readonly ResizablePaneHelper _splitGroupHelper;

    // Zen mode only reopens the panes it closed itself.
    private bool _isZenModeActive;
    private bool _wasTreeCollapsedBeforeZenMode;
    private bool _wasAssistantPanelCollapsedBeforeZenMode;

    // Ctrl+K then Z, outside the editor (the editor page recognises
    // the chord itself and forwards it whole).
    private readonly DispatcherQueueTimer _chordTimeoutTimer;
    private bool _isCtrlKArmed;

    // Each tab group's editor shows one tab at a time (design.md "Code editor"): SelectedTab if
    // it's in that group, else whatever the group showed last, else its first tab.
    private OpenFileTabViewModel? _primaryShownTab;
    private OpenFileTabViewModel? _secondaryShownTab;
    private bool _isGroupSyncQueued;
    private bool _isSyncingTabViews;

    public ExplorerViewModel ViewModel { get; }

    public ExplorerView()
    {
        ViewModel = App.GetService<ExplorerViewModel>();
        InitializeComponent();

        var localSettingsService = App.GetService<ILocalSettingsService>();

        _paneHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[0], localSettingsService, WorkspaceLayoutSettingsKeys.ExplorerTreeWidth, defaultWidth: 220);
        _paneHelper.AttachSplitter(TreeSplitter);

        _splitGroupHelper = new ResizablePaneHelper(SplitGroupGrid.ColumnDefinitions[2], localSettingsService, WorkspaceLayoutSettingsKeys.ExplorerSplitGroupWidth, defaultWidth: 320);
        _splitGroupHelper.AttachSplitter(SplitGroupSplitter);
        AssistantPanelSplitter.AddHandler(ManipulationCompletedEvent, new ManipulationCompletedEventHandler((_, _) => _assistantPanelHelper?.OnSplitterDragCompleted()), handledEventsToo: true);

        Loaded += async (_, _) =>
        {
            await _paneHelper.RestoreAsync();
            await InitializeAssistantPanelHelperAsync();

            // Starts collapsed: there's nothing in the split group until a tab is split right.
            await _splitGroupHelper.RestoreAsync();
            if (!ViewModel.HasSecondaryGroupTabs)
            {
                _splitGroupHelper.Collapse();
            }
        };

        AttachFileTab(PrimaryFileTab);

        ViewModel.ZenModeToggled += OnZenModeToggled;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.PrimaryGroupTabs.CollectionChanged += (_, _) => QueueGroupSync();
        ViewModel.SecondaryGroupTabs.CollectionChanged += (_, _) => QueueGroupSync();

        _chordTimeoutTimer = DispatcherQueue.CreateTimer();
        _chordTimeoutTimer.Interval = TimeSpan.FromSeconds(1.5);
        _chordTimeoutTimer.IsRepeating = false;
        _chordTimeoutTimer.Tick += (_, _) => DisarmCtrlK();

        // Preview: TabView handles Ctrl+Tab itself (it cycles its own tabs).
        PreviewKeyDown += OnViewPreviewKeyDown;
        PreviewKeyUp += OnViewPreviewKeyUp;
    }

    private void AttachFileTab(ExplorerFileTabControl fileTab)
    {
        fileTab.Explorer = ViewModel;
        fileTab.EditorActivated += OnEditorActivated;
        fileTab.ShortcutForwarded += OnEditorShortcutForwarded;
    }

    private async Task InitializeAssistantPanelHelperAsync()
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;

        // FlowWizard mode has Compose/Review as pages of their own, so the panel starts shut there.
        var defaultWidth = interfaceMode == InterfaceMode.FlowWizard ? 0 : 280;

        _assistantPanelHelper = new ResizablePaneHelper(WorkspaceGrid.ColumnDefinitions[4], localSettingsService, WorkspaceLayoutSettingsKeys.ExplorerAssistantPanelWidth, defaultWidth: defaultWidth);
        await _assistantPanelHelper.RestoreAsync();
    }

    private void OnToggleAssistantPanelButtonClick(object sender, RoutedEventArgs e)
    {
        _assistantPanelHelper?.ToggleCollapse();
        _assistantPanelHelper?.OnSplitterDragCompleted();
    }

    // Clicking a file opens (or activates) its tab. SelectedNode is cleared first so clicking the
    // file of a tab that was closed opens it again. Clicking a folder expands or collapses it.
    private void OnFileTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is not FileTreeNodeViewModel node)
        {
            return;
        }

        if (node.IsDirectory)
        {
            if (sender.ContainerFromItem(node) is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }

            return;
        }

        ViewModel.SelectedNode = null;
        ViewModel.SelectedNode = node;
    }

    // The "x" only asks: select the tab, then take CloseSelectedTabCommand's path, which confirms
    // before discarding unsaved edits.
    private void OnTabViewTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (args.Item is OpenFileTabViewModel tab)
        {
            ViewModel.SelectedTab = tab;
            ViewModel.CloseSelectedTabCommand.Execute(null);
        }
    }

    private void OnTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isSyncingTabViews && sender is TabView { SelectedItem: OpenFileTabViewModel tab })
        {
            ViewModel.SelectedTab = tab;
        }
    }

    // x:Load creates the split group (and its editor) when the first tab is split right, and
    // throws it away when its last tab closes.
    private void OnSecondaryGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (SecondaryFileTab is { Explorer: null } fileTab)
        {
            AttachFileTab(fileTab);
        }

        _secondaryShownTab = null;
        QueueGroupSync();
    }

    private void OnEditorActivated(object? sender, EventArgs e)
    {
        if (sender is ExplorerFileTabControl { ViewModel: { } tab })
        {
            ViewModel.SelectedTab = tab;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerViewModel.SelectedTab):
                QueueGroupSync();
                break;

            case nameof(ExplorerViewModel.HasSecondaryGroupTabs):
                if (ViewModel.HasSecondaryGroupTabs)
                {
                    _splitGroupHelper.Restore();
                }
                else
                {
                    _splitGroupHelper.Collapse();
                }

                break;

            case nameof(ExplorerViewModel.IsMruSwitcherOpen) when ViewModel.IsMruSwitcherOpen:
                MruSwitcherPopup.HorizontalOffset = Math.Max(0, (WorkspaceGrid.ActualWidth - MruSwitcherWidth) / 2);
                MruSwitcherPopup.VerticalOffset = 40;
                break;
        }
    }

    // Coalesced: a TabView reorder is a Remove then an Insert, and the group shouldn't switch
    // documents in between.
    private void QueueGroupSync()
    {
        if (_isGroupSyncQueued)
        {
            return;
        }

        _isGroupSyncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _isGroupSyncQueued = false;
            SyncGroups();
        });
    }

    private void SyncGroups()
    {
        _primaryShownTab = SyncGroup(ViewModel.PrimaryGroupTabs, _primaryShownTab, PrimaryTabView, PrimaryFileTab);

        if (SecondaryFileTab is { } secondaryFileTab && SecondaryTabView is { } secondaryTabView)
        {
            _secondaryShownTab = SyncGroup(ViewModel.SecondaryGroupTabs, _secondaryShownTab, secondaryTabView, secondaryFileTab);
        }
    }

    private OpenFileTabViewModel? SyncGroup(IList<OpenFileTabViewModel> groupTabs, OpenFileTabViewModel? shownTab, TabView tabView, ExplorerFileTabControl fileTab)
    {
        var tab = ViewModel.SelectedTab is { } selected && groupTabs.Contains(selected)
            ? selected
            : shownTab is not null && groupTabs.Contains(shownTab) ? shownTab : groupTabs.FirstOrDefault();

        _isSyncingTabViews = true;
        tabView.SelectedItem = tab;
        _isSyncingTabViews = false;

        fileTab.ShowTab(tab);
        fileTab.CloseDocumentsExcept(groupTabs.Select(t => t.FilePath));
        return tab;
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

    // Keys the editor's WebView2 swallowed (CodeEditor.ShortcutForwarded). The Quick Open, command
    // palette and Go to Symbol view models are app-wide singletons, the same ones ShellView's
    // accelerators open.
    private void OnEditorShortcutForwarded(string gesture)
    {
        switch (gesture)
        {
            case "Ctrl+P":
                App.GetService<QuickOpenViewModel>().OpenCommand.Execute(null);
                break;

            case "Ctrl+Shift+P":
                App.GetService<CommandPaletteViewModel>().OpenCommand.Execute(null);
                break;

            case "Ctrl+Shift+O":
                App.GetService<SymbolQuickPickViewModel>().OpenCommand.Execute(null);
                break;

            case "Ctrl+Tab":
                OpenOrAdvanceMruSwitcher();
                break;

            case "CtrlReleased" when ViewModel.IsMruSwitcherOpen:
                ViewModel.CommitMruSwitcher();
                break;

            case "Ctrl+K Z":
                ViewModel.ToggleZenModeCommand.Execute(null);
                break;
        }
    }

    private void OpenOrAdvanceMruSwitcher()
    {
        if (ViewModel.IsMruSwitcherOpen)
        {
            ViewModel.AdvanceMruSwitcher();
        }
        else
        {
            ViewModel.OpenMruSwitcher();
        }
    }

    private void DisarmCtrlK()
    {
        _isCtrlKArmed = false;
        _chordTimeoutTimer.Stop();
    }

    // Ctrl+K then Z toggles zen mode; Ctrl+Tab (held, repeated)
    // opens/advances the MRU switcher and releasing Ctrl commits it.
    private void OnViewPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var isCtrlDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Tab && isCtrlDown)
        {
            OpenOrAdvanceMruSwitcher();
            e.Handled = true;
            return;
        }

        if (_isCtrlKArmed && e.Key == VirtualKey.Z)
        {
            DisarmCtrlK();
            ViewModel.ToggleZenModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == VirtualKey.K && isCtrlDown)
        {
            _isCtrlKArmed = true;
            _chordTimeoutTimer.Stop();
            _chordTimeoutTimer.Start();
            e.Handled = true;
        }
    }

    private void OnViewPreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl && ViewModel.IsMruSwitcherOpen)
        {
            ViewModel.CommitMruSwitcher();
        }
    }
}
