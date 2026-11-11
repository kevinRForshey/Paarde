using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

using VirtualKey = Windows.System.VirtualKey;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Helpers;
using Paarde.WinUI.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class ShellView : UserControl
{
    // PalettePopupBorderStyle's Width in ShellView.xaml.
    private const double PalettePopupWidth = 480;

    private readonly ObservableCollection<ProblemRow> _problemRows = [];

    public ShellViewModel ViewModel
    {
        get;
    }

    public ShellView()
    {
        ViewModel = App.GetService<ShellViewModel>();
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // specs/terminal-emulation T6 / ide-workbench-shell R1.3: the dock's terminals stay
        // ignorant of IPtyProcessManager; the view models expose thin passthroughs.
        OutputTerminal.InputReceived += ViewModel.RunPanelViewModel.WriteInput;
        OutputTerminal.Resized += ViewModel.RunPanelViewModel.Resize;
        DockTerminal.InputReceived += ViewModel.WorkbenchDockViewModel.TerminalViewModel.WriteInput;
        DockTerminal.Resized += ViewModel.WorkbenchDockViewModel.TerminalViewModel.Resize;

        // A run grabs keyboard focus for its output terminal, the way a real terminal does.
        ViewModel.RunPanelViewModel.PropertyChanged += OnRunPanelViewModelPropertyChanged;

        // Selector bar <-> ActiveTabKey, both ways.
        ShowDockTab(ViewModel.WorkbenchDockViewModel.ActiveTabKey);
        ViewModel.WorkbenchDockViewModel.PropertyChanged += OnWorkbenchDockViewModelPropertyChanged;

        // DiagnosticsAggregator.Diagnostics is a per-file dictionary; the Problems list needs
        // one flat row per diagnostic.
        ProblemsList.ItemsSource = _problemRows;
        RefreshProblemRows();
        ViewModel.WorkbenchDockViewModel.DiagnosticsAggregator.Changed += OnDiagnosticsAggregatorChanged;

        // Centre each popup and move keyboard focus into its query box the
        // moment it opens - nothing else in the popup would take it.
        ViewModel.QuickOpenViewModel.PropertyChanged += (_, e) =>
            OnPalettePopupPropertyChanged(e, ViewModel.QuickOpenViewModel.IsOpen, QuickOpenPopup, QuickOpenQueryBox);
        ViewModel.CommandPaletteViewModel.PropertyChanged += (_, e) =>
            OnPalettePopupPropertyChanged(e, ViewModel.CommandPaletteViewModel.IsOpen, CommandPalettePopup, CommandPaletteQueryBox);
        ViewModel.SymbolQuickPickViewModel.PropertyChanged += (_, e) =>
            OnPalettePopupPropertyChanged(e, ViewModel.SymbolQuickPickViewModel.IsOpen, SymbolQuickPickPopup, SymbolQuickPickQueryBox);

        InsertFlowWizardMenuItemsIfNeededAsync();
    }

    // NavigationView.SelectedItem is the raw NavigationViewItem; the header shows that item's label.
    // Instance (not static) because x:Bind's generated code calls it through the page instance.
    public object? GetHeader(object? selected) => (selected as NavigationViewItem)?.Content;

    // Avalonia's binding used StringFormat='{}{0} dirty', which x:Bind doesn't have.
    public string FormatDirtyCount(int count) => $"{count} dirty";

    private void OnQuickOpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.QuickOpenViewModel.OpenCommand.Execute(null);
        args.Handled = true;
    }

    private void OnCommandPaletteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.CommandPaletteViewModel.OpenCommand.Execute(null);
        args.Handled = true;
    }

    private void OnSymbolQuickPickAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.SymbolQuickPickViewModel.OpenCommand.Execute(null);
        args.Handled = true;
    }

    private void OnPalettePopupPropertyChanged(PropertyChangedEventArgs e, bool isOpen, Popup popup, TextBox queryBox)
    {
        if (e.PropertyName != nameof(QuickOpenViewModel.IsOpen) || !isOpen)
        {
            return;
        }

        // The popup's origin is row 1's top-left; its Border has a fixed width.
        popup.HorizontalOffset = Math.Max(0, (ActualWidth - PalettePopupWidth) / 2);
        popup.VerticalOffset = 8;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => queryBox.Focus(FocusState.Programmatic));
    }

    private void OnQuickOpenQueryBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandlePaletteKey(
            e.Key,
            QuickOpenResultsList,
            ViewModel.QuickOpenViewModel.CloseCommand,
            ViewModel.QuickOpenViewModel.ActivateSelectedCommand);

    private void OnQuickOpenResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.QuickOpenViewModel.ActivateSelectedCommand.Execute(null);

    private void OnCommandPaletteQueryBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandlePaletteKey(
            e.Key,
            CommandPaletteResultsList,
            ViewModel.CommandPaletteViewModel.CloseCommand,
            ViewModel.CommandPaletteViewModel.ExecuteSelectedCommand);

    private void OnCommandPaletteResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.CommandPaletteViewModel.ExecuteSelectedCommand.Execute(null);

    private void OnSymbolQuickPickQueryBoxPreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        e.Handled = HandlePaletteKey(
            e.Key,
            SymbolQuickPickResultsList,
            ViewModel.SymbolQuickPickViewModel.CloseCommand,
            ViewModel.SymbolQuickPickViewModel.ActivateSelectedCommand);

    private void OnSymbolQuickPickResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) =>
        ViewModel.SymbolQuickPickViewModel.ActivateSelectedCommand.Execute(null);

    // Up/Down move the ListView's own selection (which SelectedResult is two-way bound to),
    // same as Avalonia's MoveListBoxSelection. PreviewKeyDown because the TextBox handles Up/Down
    // itself before KeyDown.
    private static bool HandlePaletteKey(VirtualKey key, ListView resultsList, ICommand close, ICommand activate)
    {
        switch (key)
        {
            case VirtualKey.Escape:
                close.Execute(null);
                return true;

            case VirtualKey.Enter:
                activate.Execute(null);
                return true;

            case VirtualKey.Down:
                MoveListSelection(resultsList, 1);
                return true;

            case VirtualKey.Up:
                MoveListSelection(resultsList, -1);
                return true;

            default:
                return false;
        }
    }

    private static void MoveListSelection(ListView listView, int delta)
    {
        var count = listView.Items.Count;
        if (count == 0)
        {
            return;
        }

        var currentIndex = listView.SelectedIndex < 0 ? -1 : listView.SelectedIndex;
        listView.SelectedIndex = ((currentIndex + delta) % count + count) % count;
        listView.ScrollIntoView(listView.SelectedItem);
    }

    private void OnRunPanelViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunPanelViewModel.IsRunning) && ViewModel.RunPanelViewModel.IsRunning)
        {
            // Low priority so the dock's expand/tab switch (raised in the same call stack) has been
            // laid out before focus moves - Avalonia defers to DispatcherPriority.Loaded for the
            // same reason.
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => OutputTerminal.Focus(FocusState.Programmatic));
        }
    }

    private void OnWorkbenchDockViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchDockViewModel.ActiveTabKey))
        {
            ShowDockTab(ViewModel.WorkbenchDockViewModel.ActiveTabKey);
        }
    }

    private void OnDockSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string key)
        {
            ViewModel.WorkbenchDockViewModel.ActiveTabKey = key;
            ShowDockTab(key);
        }
    }

    private void ShowDockTab(string key)
    {
        var item = DockSelectorBar.Items.FirstOrDefault(i => (string)i.Tag == key) ?? DockSelectorBar.Items[1];
        if (!ReferenceEquals(DockSelectorBar.SelectedItem, item))
        {
            DockSelectorBar.SelectedItem = item;
        }

        var activeKey = (string)item.Tag;
        BuildTerminal.Visibility = activeKey == "Build" ? Visibility.Visible : Visibility.Collapsed;
        OutputTerminal.Visibility = activeKey == "Run" ? Visibility.Visible : Visibility.Collapsed;
        ProblemsList.Visibility = activeKey == "Problems" ? Visibility.Visible : Visibility.Collapsed;
        DockTerminalPane.Visibility = activeKey == "Terminal" ? Visibility.Visible : Visibility.Collapsed;

        // Clicking the Terminal tab should let the user type straight into it.
        if (activeKey == "Terminal")
        {
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => DockTerminal.Focus(FocusState.Programmatic));
        }
    }

    // Enter in the dock's command box runs the command, same as TerminalView's own command box.
    private void OnDockCommandTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var terminalViewModel = ViewModel.WorkbenchDockViewModel.TerminalViewModel;
        if (e.Key == VirtualKey.Enter && terminalViewModel.RunCommandCommand.CanExecute(null))
        {
            terminalViewModel.RunCommandCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Changed fires off the UI thread.
    private void OnDiagnosticsAggregatorChanged() => DispatcherQueue.TryEnqueue(RefreshProblemRows);

    private void RefreshProblemRows()
    {
        _problemRows.Clear();

        foreach (var (filePath, diagnostics) in ViewModel.WorkbenchDockViewModel.DiagnosticsAggregator.Diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                _problemRows.Add(new ProblemRow(filePath, diagnostic.Start, diagnostic.Severity, diagnostic.Message));
            }
        }
    }

    // FlowWizard mode gets Home/Compose/Review menu items ahead
    // of EditorFocused mode's Explorer/Terminal/History - inserted here rather than in XAML since
    // which set applies is a runtime setting. Fire-and-forget like Paarde.Avalonia's: the only effect
    // of this resolving after the first navigation is that navigation's selected-item highlight,
    // which self-corrects on the next one.
    private async void InsertFlowWizardMenuItemsIfNeededAsync()
    {
        var localSettingsService = App.GetService<ILocalSettingsService>();
        var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
        if (interfaceMode != InterfaceMode.FlowWizard)
        {
            return;
        }

        var homeItem = new NavigationViewItem { Content = "Home", Icon = new SymbolIcon(Symbol.Home) };
        NavigationHelper.SetNavigateTo(homeItem, typeof(MainViewModel).FullName);

        var composeItem = new NavigationViewItem { Content = "Compose", Icon = new SymbolIcon(Symbol.Send) };
        NavigationHelper.SetNavigateTo(composeItem, typeof(PromptViewModel).FullName);

        var reviewItem = new NavigationViewItem { Content = "Review", Icon = new SymbolIcon(Symbol.Document) };
        NavigationHelper.SetNavigateTo(reviewItem, typeof(TutorialViewModel).FullName);

        NavigationViewControl.MenuItems.Insert(0, reviewItem);
        NavigationViewControl.MenuItems.Insert(0, composeItem);
        NavigationViewControl.MenuItems.Insert(0, homeItem);
    }
}
