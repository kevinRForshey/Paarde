using System.ComponentModel;

using Avalonia.Controls;
using Avalonia.Threading;

using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class ShellView : UserControl
{
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
    }

    private void OnRunPanelViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunPanelViewModel.IsRunning) && ViewModel.RunPanelViewModel.IsRunning)
        {
            // Bug found via a headless harness reproducing this exact PropertyChanged sequence
            // (reported as "the app clearly runs and shows output, but it won't take any
            // input"): Porta.Pty's Linux StartTerminalAsync is fully synchronous (forkpty via a
            // native call, wrapped in Task.FromResult), so `await _ptyProcessManager.StartAsync`
            // in RunPanelViewModel.RunAsync never actually yields - IsOutputExpanded = true and
            // IsRunning = true both fire within the same synchronous call stack as the Run button
            // click, before Avalonia's layout pass ever expands the (collapsed-by-default)
            // Expander. Calling Focus() here directly targets a still-0x0 OutputTerminal and
            // silently fails - confirmed via the harness: Focus() returns false and IsFocused
            // stays false, even though the Expander visibly finishes expanding moments later.
            // Deferring to DispatcherPriority.Loaded - confirmed via the same harness to run after
            // the pending layout/render pass - lets the Expander's layout settle first, so
            // OutputTerminal has real bounds by the time Focus() actually runs.
            Dispatcher.UIThread.Post(() => OutputTerminal.Focus(), DispatcherPriority.Loaded);
        }
    }
}
