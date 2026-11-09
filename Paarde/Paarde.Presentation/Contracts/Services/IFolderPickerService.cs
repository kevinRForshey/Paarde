namespace Paarde.Presentation.Contracts.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync();
}
