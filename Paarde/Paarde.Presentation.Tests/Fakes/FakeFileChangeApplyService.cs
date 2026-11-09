using Paarde.Core.Contracts.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeFileChangeApplyService : IFileChangeApplyService
{
    public int ApplyCallCount { get; private set; }

    public int WriteRawCallCount { get; private set; }

    public string? LastWriteRawFilePath { get; private set; }

    public string? LastWriteRawContent { get; private set; }

    public Exception? ExceptionToThrow { get; set; }

    public void Apply(ProposedFileChangeViewModel file, string workingDirectoryPath)
    {
        ApplyCallCount++;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }

    public void WriteRaw(string filePath, string content, string workingDirectoryPath)
    {
        WriteRawCallCount++;
        LastWriteRawFilePath = filePath;
        LastWriteRawContent = content;

        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}
