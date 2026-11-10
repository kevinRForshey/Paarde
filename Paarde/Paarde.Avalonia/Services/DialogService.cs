
using FluentAvalonia.UI.Controls;
using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Services;

public class DialogService : IDialogService
{
    public async Task<bool> ConfirmAsync(string title, string message, string confirmButtonText, string cancelButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmButtonText,
            CloseButtonText = cancelButtonText,
            // The destructive choice (discard edits) should never be the one Enter/default-focus
            // picks by accident.
            DefaultButton = ContentDialogButton.Close,
        };

        // Parameterless ShowAsync() resolves the owning window itself; the app only ever has one
        // (MainWindow), so there's no ambiguity to hand it explicitly.
        var result = await dialog.ShowAsync();

        return result == ContentDialogResult.Primary;
    }

    public async Task InfoAsync(string title, string message, string closeButtonText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = closeButtonText,
        };

        await dialog.ShowAsync();
    }
}
