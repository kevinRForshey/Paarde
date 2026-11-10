using Paarde.Core.ViewModels;

namespace Paarde.Core.Contracts.Services;

public interface IFileChangeApplyService
{
    void Apply(ProposedFileChangeViewModel file, string workingDirectoryPath);

    // Writes raw content back to a path under workingDirectoryPath, preserving whatever
    // encoding/line endings are already on disk there - used by undo/revert to restore a
    // prior snapshot without a ProposedFileChangeViewModel to compose content from.
    void WriteRaw(string filePath, string content, string workingDirectoryPath);
}
