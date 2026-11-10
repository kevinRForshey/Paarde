using Microsoft.Windows.Storage.Pickers;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

public class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync()
    {
        if (App.MainWindow is not { } window)
        {
            return null;
        }

        var folder = await new FolderPicker(window.AppWindow.Id).PickSingleFolderAsync();

        return folder?.Path;
    }
}
