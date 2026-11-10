using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeFolderPickerService : IFolderPickerService
{
    public string? FolderToReturn { get; set; }

    public Task<string?> PickFolderAsync() => Task.FromResult(FolderToReturn);
}
