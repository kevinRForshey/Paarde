using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Services;

public class WorkingDirectoryContext : IWorkingDirectoryContext
{
    public event Action<string>? Changed;

    public void SetCurrent(string workingDirectoryPath) => Changed?.Invoke(workingDirectoryPath);
}
