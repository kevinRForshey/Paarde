using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeApplyHistoryLog : IApplyHistoryLog
{
    private readonly List<ApplyHistoryEntry> _entries = new();

    public IReadOnlyList<ApplyHistoryEntry> Entries => _entries;

    public int RecordCallCount { get; private set; }

    public void Record(ApplyHistoryEntryKind kind, IReadOnlyList<string> filePaths)
    {
        RecordCallCount++;
        _entries.Add(new ApplyHistoryEntry(DateTimeOffset.UtcNow, kind, filePaths));
    }

    public void Restore(IReadOnlyList<ApplyHistoryEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
    }
}
