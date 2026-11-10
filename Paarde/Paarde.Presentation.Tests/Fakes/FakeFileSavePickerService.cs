using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeFileSavePickerService : IFileSavePickerService
{
    public string? PathToReturn { get; set; }

    public string? LastSuggestedFileName { get; private set; }

    public Task<string?> PickSaveFileAsync(string title, string suggestedFileName, string fileTypeName, string[] extensions)
    {
        LastSuggestedFileName = suggestedFileName;
        return Task.FromResult(PathToReturn);
    }
}
