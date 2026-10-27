using System.ComponentModel;

using Avalonia.Controls;

using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Views;

public partial class TerminalView : UserControl
{
    public TerminalViewModel ViewModel
    {
        get;
    }

    public TerminalView()
    {
        ViewModel = App.GetService<TerminalViewModel>();
        DataContext = ViewModel;
        InitializeComponent();

        // specs/terminal-emulation T7: OutputTerminal stays ignorant of IPtyProcessManager, same
        // shape ShellView.axaml.cs already uses for RunPanelViewModel's TerminalControl.
        OutputTerminal.InputReceived += ViewModel.WriteInput;
        OutputTerminal.Resized += ViewModel.Resize;

        // Same follow-up fix ShellView.axaml.cs already applies to its own output terminal:
        // nothing moves keyboard focus into OutputTerminal on its own, so keystrokes would keep
        // going wherever focus already was unless the user happened to click into the output area
        // first.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.IsRunning) && ViewModel.IsRunning)
        {
            OutputTerminal.Focus();
        }
    }
}
