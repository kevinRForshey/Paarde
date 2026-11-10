using Avalonia.Platform.Storage;

using Paarde.Presentation.Contracts.Services;

namespace Paarde.Avalonia.Services;

public class FileSavePickerService : IFileSavePickerService
{
    public async Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string[] extensions)
    {
        var topLevel = App.MainWindow;
        if (topLevel == null)
        {
            return null;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices = [new FilePickerFileType(fileTypeName) { Patterns = extensions.Select(e => $"*.{e}").ToArray() }],
        });

        return file?.TryGetLocalPath();
    }
}
