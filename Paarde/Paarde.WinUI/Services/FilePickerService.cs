using Microsoft.Windows.Storage.Pickers;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

// Windows App SDK's own pickers (Microsoft.Windows.Storage.Pickers), not the UWP-era
// Windows.Storage.Pickers: these take the window's id directly instead of needing
// InitializeWithWindow interop, return plain paths, and also work when the app runs elevated.
public class FilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        if (App.MainWindow is not { } window)
        {
            return Array.Empty<string>();
        }

        var picker = new FileOpenPicker(window.AppWindow.Id);
        picker.FileTypeFilter.Add("*");

        var files = await picker.PickMultipleFilesAsync();

        return files.Select(file => file.Path).ToArray();
    }
}
