using Microsoft.Windows.Storage.Pickers;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.WinUI.Services;

public class FileSavePickerService : IFileSavePickerService
{
    // title is unused: unlike Avalonia's, the Windows picker has no settable title.
    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string[] extensions)
    {
        if (App.MainWindow is not { } window)
        {
            return null;
        }

        var picker = new FileSavePicker(window.AppWindow.Id)
        {
            SuggestedFileName = suggestedFileName,
        };
        picker.FileTypeChoices.Add(fileTypeName, extensions.Select(e => $".{e}").ToList());

        var file = await picker.PickSaveFileAsync();

        return file?.Path;
    }
}
