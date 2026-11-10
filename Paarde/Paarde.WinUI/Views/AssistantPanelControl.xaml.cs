using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.ViewModels;

namespace Paarde.WinUI.Views;

public sealed partial class AssistantPanelControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(nameof(ViewModel), typeof(AssistantPanelViewModel), typeof(AssistantPanelControl), new PropertyMetadata(null, OnViewModelChanged));

    // Set by ExplorerView. AssistantPanelViewModel is a singleton that outlives this control, so its
    // PropertyChanged subscription only lives while the control is loaded.
    public AssistantPanelViewModel? ViewModel
    {
        get => (AssistantPanelViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public AssistantPanelControl()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
                ShowTab(viewModel.SelectedTabIndex);
            }
        };
        Unloaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        };
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AssistantPanelControl)d;
        control.Bindings.Update();
        if (control.ViewModel is { } viewModel)
        {
            control.ShowTab(viewModel.SelectedTabIndex);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssistantPanelViewModel.SelectedTabIndex) && ViewModel is { } viewModel)
        {
            ShowTab(viewModel.SelectedTabIndex);
        }
    }

    private void OnTabSelectorBarSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var index = sender.Items.IndexOf(sender.SelectedItem);
        if (index >= 0 && ViewModel is { } viewModel)
        {
            viewModel.SelectedTabIndex = index;
            ShowTab(index);
        }
    }

    private void ShowTab(int index)
    {
        var item = TabSelectorBar.Items[Math.Clamp(index, 0, 1)];
        if (!ReferenceEquals(TabSelectorBar.SelectedItem, item))
        {
            TabSelectorBar.SelectedItem = item;
        }

        ComposePane.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewPane.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
