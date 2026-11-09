using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

public class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmButtonText, string cancelButtonText)
    {
        var dialog = CreateDialog(title, message, closeButtonText: cancelButtonText);
        dialog.PrimaryButtonText = confirmButtonText;

        // The destructive choice (discard edits) should never be the one Enter/default-focus picks
        // by accident.
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task InfoAsync(string title, string message, string closeButtonText)
    {
        await CreateDialog(title, message, closeButtonText).ShowAsync();
    }

    // Unlike FluentAvalonia's ContentDialog, WinUI's has to be told which XamlRoot (window) to show
    // in, and doesn't inherit the app's chosen theme on its own since it's hosted in a popup layer.
    private static ContentDialog CreateDialog(string title, string message, string closeButtonText)
    {
        var root = App.MainWindow?.Content as FrameworkElement
            ?? throw new InvalidOperationException("A dialog can't be shown before the main window has content.");

        return new ContentDialog
        {
            XamlRoot = root.XamlRoot,
            RequestedTheme = root.RequestedTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = closeButtonText,
        };
    }
}
