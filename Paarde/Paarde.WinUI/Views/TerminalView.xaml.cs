using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class TerminalView : Page
{
    public TerminalViewModel ViewModel
    {
        get;
    }

    public TerminalView()
    {
        ViewModel = App.GetService<TerminalViewModel>();
        InitializeComponent();

        // OutputTerminal stays ignorant of IPtyProcessManager, same as Avalonia's.
        OutputTerminal.InputReceived += ViewModel.WriteInput;
        OutputTerminal.Resized += ViewModel.Resize;

        // Nothing moves keyboard focus into OutputTerminal on its own, so a started command grabs
        // it, the way a real terminal does. TerminalViewModel is a singleton that outlives this
        // page, so the subscription is tied to the page being in the tree.
        Loaded += (_, _) => ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.IsRunning) && ViewModel.IsRunning)
        {
            OutputTerminal.Focus(FocusState.Programmatic);
        }
    }
}
