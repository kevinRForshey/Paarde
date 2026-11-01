using Paarde.Avalonia.Contracts.Services;

namespace Paarde.Avalonia.Services;

public class WorkingDirectoryContext : IWorkingDirectoryContext
{
    public event Action<string>? Changed;

    public void SetCurrent(string workingDirectoryPath) => Changed?.Invoke(workingDirectoryPath);
}
