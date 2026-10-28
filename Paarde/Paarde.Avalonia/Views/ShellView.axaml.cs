using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Avalonia.Controls;
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
    }

    private void OnDockTabControlSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var index = DockTabControl.SelectedIndex;
        if (index >= 0 && index < TabKeysByIndex.Length)
        {
            ViewModel.WorkbenchDockViewModel.ActiveTabKey = TabKeysByIndex[index];
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
