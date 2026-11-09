using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeFilePickerService : IFilePickerService
{
    public IReadOnlyList<string> FilesToReturn { get; set; } = Array.Empty<string>();

    public Task<IReadOnlyList<string>> PickFilesAsync() => Task.FromResult(FilesToReturn);
}
