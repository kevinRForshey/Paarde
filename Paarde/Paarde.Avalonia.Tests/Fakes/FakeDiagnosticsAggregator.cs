using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeDiagnosticsAggregator : IDiagnosticsAggregator
{
    public Dictionary<string, IReadOnlyList<LspDiagnostic>> DiagnosticsToReturn { get; } = new(StringComparer.Ordinal);

    public int ChangedRaisedCount { get; private set; }

    public event Action? Changed;

    public IReadOnlyDictionary<string, IReadOnlyList<LspDiagnostic>> Diagnostics => DiagnosticsToReturn;

    public void RaiseChanged()
    {
        ChangedRaisedCount++;
        Changed?.Invoke();
    }
}
