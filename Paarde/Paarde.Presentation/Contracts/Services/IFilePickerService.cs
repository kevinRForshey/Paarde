namespace Paarde.Presentation.Contracts.Services;

public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickFilesAsync();
}
