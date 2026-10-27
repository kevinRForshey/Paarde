using System.ComponentModel;

using Avalonia.Controls;

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
            OutputTerminal.Focus();
        }
    }
}
