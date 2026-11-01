using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ShellView : UserControl
{
    // specs/ide-workbench-shell R1: keys WorkbenchDockViewModel.ActiveTabKey persists/compares
    // against, in the same order the TabItems appear in ShellView.axaml - the fallback design.md
    // flagged in case TabControl.SelectedItem can't bind directly against a string Header.
    private static readonly string[] TabKeysByIndex = ["Build", "Run", "Problems", "Terminal"];

    private readonly ObservableCollection<ProblemRow> _problemRows = [];

    public ShellViewModel ViewModel
    {
        get;
    }

    public ShellView()
    {
        ViewModel = App.GetService<ShellViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        ViewModel.NavigationService.Frame = NavigationFrame;
        ViewModel.NavigationViewService.Initialize(NavigationViewControl);

        // specs/terminal-emulation T6: OutputTerminal stays ignorant of IPtyProcessManager (same
        // shape as Editor.GhostTextRequested above in ExplorerView.axaml.cs) - RunPanelViewModel
        // exposes thin passthroughs rather than this code-behind reaching past it to the service.
        OutputTerminal.InputReceived += ViewModel.RunPanelViewModel.WriteInput;
        OutputTerminal.Resized += ViewModel.RunPanelViewModel.Resize;

        // Follow-up fix (reported as "typing into the app running in the terminal did nothing"):
        // nothing moved keyboard focus into OutputTerminal, so keystrokes kept going wherever
        // focus already was (the Run button, which ignores letter keys) unless the user happened
        // to click into the output strip first. A real terminal grabs focus the moment something
        // interactive starts running in it - same PropertyChanged-reacts-in-code-behind pattern
        // TerminalView.axaml.cs already uses for its own View-only concern (caret-follows-output).
        ViewModel.RunPanelViewModel.PropertyChanged += OnRunPanelViewModelPropertyChanged;

        // specs/ide-workbench-shell R1.3: same InputReceived/Resized passthrough shape as
        // OutputTerminal above, reaching the shared WorkbenchDockViewModel.TerminalViewModel
        // instance instead of RunPanelViewModel's.
        DockTerminal.InputReceived += ViewModel.WorkbenchDockViewModel.TerminalViewModel.WriteInput;
        DockTerminal.Resized += ViewModel.WorkbenchDockViewModel.TerminalViewModel.Resize;

        // specs/ide-workbench-shell R1.1/R2.1: DockTabControl.SelectedIndex <-> ActiveTabKey, kept
        // in sync both ways in code-behind rather than a direct XAML binding - TabControl's
        // SelectedItem binds against the selected TabItem instance, not its Header, so a plain
        // string<->SelectedItem binding wouldn't resolve to anything.
        DockTabControl.SelectedIndex = Math.Max(Array.IndexOf(TabKeysByIndex, ViewModel.WorkbenchDockViewModel.ActiveTabKey), 0);
        ViewModel.WorkbenchDockViewModel.PropertyChanged += OnWorkbenchDockViewModelPropertyChanged;
        DockTabControl.SelectionChanged += OnDockTabControlSelectionChanged;

        // specs/ide-workbench-shell R1.4: ProblemsList is populated/kept in sync here rather than
        // bound directly in XAML, since DiagnosticsAggregator.Diagnostics is a per-file dictionary
        // and the list needs a flat, display-ready row per diagnostic (design.md: "a pure
        // presentation flattening... not a new ViewModel-level collection type").
        ProblemsList.ItemsSource = _problemRows;
        RefreshProblemRows();
        ViewModel.WorkbenchDockViewModel.DiagnosticsAggregator.Changed += OnDiagnosticsAggregatorChanged;

        // Epic 24, R1.3/R2.5/R3.3: move keyboard focus into the popup's own query box the moment
        // it opens - neither popup has anything else to give it, and without this the global
        // Ctrl+P/Ctrl+Shift+P KeyBinding would leave focus wherever it already was.
        ViewModel.QuickOpenViewModel.PropertyChanged += OnQuickOpenViewModelPropertyChanged;
        ViewModel.CommandPaletteViewModel.PropertyChanged += OnCommandPaletteViewModelPropertyChanged;

        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    // Epic 24, R3.1: Ctrl+P/Ctrl+Shift+P must work regardless of which control has focus - the
    // only place that's true app-wide is the Window's own KeyBindings (TopLevel.KeyBindings);
    // ShellView itself is a UserControl, which has no such collection. App.axaml.cs's F12
    // DevTools gesture is the one existing precedent for attaching a KeyGesture this way.
    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Control),
            Command = ViewModel.QuickOpenViewModel.OpenCommand,
        });

        window.KeyBindings.Add(new KeyBinding
        {
            Gesture = new KeyGesture(Key.P, KeyModifiers.Control | KeyModifiers.Shift),
            Command = ViewModel.CommandPaletteViewModel.OpenCommand,
        });
    }

    private void OnQuickOpenViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickOpenViewModel.IsOpen) && ViewModel.QuickOpenViewModel.IsOpen)
        {
            Dispatcher.UIThread.Post(() => QuickOpenQueryBox.Focus());
        }
    }

    private void OnCommandPaletteViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CommandPaletteViewModel.IsOpen) && ViewModel.CommandPaletteViewModel.IsOpen)
        {
            Dispatcher.UIThread.Post(() => CommandPaletteQueryBox.Focus());
        }
    }

    // R1.3: Up/Down move QuickOpenResultsList's own selection (which SelectedResult is two-way
    // bound to) rather than touching the view model directly - same "code-behind moves the
    // ListBox, not the VM property" shape PromptView.axaml.cs's MoveMentionSelection already uses.
    private void OnQuickOpenQueryBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.QuickOpenViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel.QuickOpenViewModel.ActivateSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                MoveListBoxSelection(QuickOpenResultsList, 1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveListBoxSelection(QuickOpenResultsList, -1);
                e.Handled = true;
                break;
        }
    }

    private void OnQuickOpenResultDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.QuickOpenViewModel.ActivateSelectedCommand.Execute(null);

    private void OnCommandPaletteQueryBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ViewModel.CommandPaletteViewModel.CloseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel.CommandPaletteViewModel.ExecuteSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                MoveListBoxSelection(CommandPaletteResultsList, 1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveListBoxSelection(CommandPaletteResultsList, -1);
                e.Handled = true;
                break;
        }
    }

    private void OnCommandPaletteResultDoubleTapped(object? sender, TappedEventArgs e) =>
        ViewModel.CommandPaletteViewModel.ExecuteSelectedCommand.Execute(null);

    private static void MoveListBoxSelection(ListBox listBox, int delta)
    {
        var count = listBox.ItemCount;
        if (count == 0)
        {
            return;
        }

        var currentIndex = listBox.SelectedIndex < 0 ? -1 : listBox.SelectedIndex;
        listBox.SelectedIndex = ((currentIndex + delta) % count + count) % count;
    }

    private void OnRunPanelViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunPanelViewModel.IsRunning) && ViewModel.RunPanelViewModel.IsRunning)
        {
            // Bug found via a headless harness reproducing this exact PropertyChanged sequence
            // (reported as "the app clearly runs and shows output, but it won't take any
            // input"): Porta.Pty's Linux StartTerminalAsync is fully synchronous (forkpty via a
            // native call, wrapped in Task.FromResult), so `await _ptyProcessManager.StartAsync`
            // in RunPanelViewModel.RunAsync never actually yields - WorkbenchDockViewModel's
            // IsExpanded = true and IsRunning = true both fire within the same synchronous call
            // stack as the Run button click, before Avalonia's layout pass ever expands the
            // (collapsed-by-default) Expander. Calling Focus() here directly targets a still-0x0
            // OutputTerminal and silently fails - confirmed via the harness: Focus() returns
            // false and IsFocused stays false, even though the Expander visibly finishes
            // expanding moments later. Deferring to DispatcherPriority.Loaded - confirmed via the
            // same harness to run after the pending layout/render pass - lets the Expander's
            // layout settle first, so OutputTerminal has real bounds by the time Focus() actually
            // runs.
            Dispatcher.UIThread.Post(() => OutputTerminal.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void OnWorkbenchDockViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorkbenchDockViewModel.ActiveTabKey))
        {
            return;
        }

        var index = Math.Max(Array.IndexOf(TabKeysByIndex, ViewModel.WorkbenchDockViewModel.ActiveTabKey), 0);
        if (DockTabControl.SelectedIndex != index)
        {
            DockTabControl.SelectedIndex = index;
        }

        FocusDockTerminalIfActive();
    }

    private void OnDockTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = DockTabControl.SelectedIndex;
        if (index >= 0 && index < TabKeysByIndex.Length)
        {
            ViewModel.WorkbenchDockViewModel.ActiveTabKey = TabKeysByIndex[index];
        }

        FocusDockTerminalIfActive();
    }

    // Bug reported as "the console built into the IDE doesn't show my input": unlike OutputTerminal
    // above (focused via OnRunPanelViewModelPropertyChanged whenever a run starts), nothing ever
    // moved keyboard focus into DockTerminal - clicking the Terminal tab just changed which
    // TabItem's content was visible, so keystrokes kept going wherever focus already was. Called
    // from both directions ActiveTabKey can change (user clicking the tab, or code setting
    // ActiveTabKey directly) so the dock terminal always ends up focused once it's the visible tab.
    private void FocusDockTerminalIfActive()
    {
        if (ViewModel.WorkbenchDockViewModel.ActiveTabKey == "Terminal")
        {
            DockTerminal.Focus();
        }
    }

    private void OnDiagnosticsAggregatorChanged() => Dispatcher.UIThread.Post(RefreshProblemRows);

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
}
