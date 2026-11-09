using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeWorkingDirectoryContext : IWorkingDirectoryContext
{
    public List<string> SetCurrentCalls { get; } = [];

    public event Action<string>? Changed;

    public void SetCurrent(string workingDirectoryPath)
    {
        SetCurrentCalls.Add(workingDirectoryPath);
        Changed?.Invoke(workingDirectoryPath);
    }
}
