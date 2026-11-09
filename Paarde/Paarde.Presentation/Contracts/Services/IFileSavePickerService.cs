namespace Paarde.Presentation.Contracts.Services;

public interface IFileSavePickerService
{
    // Returns the local path the user picked, or null if they cancelled. fileTypeName/extensions
    // drive the picker's file-type filter (e.g. "Markdown", ["md"]) - extensions without the
    // leading dot, matching Avalonia's FilePickerFileType.Patterns glob format.
    Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string[] extensions);
}
