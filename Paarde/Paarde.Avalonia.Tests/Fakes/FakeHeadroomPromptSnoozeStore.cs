using Paarde.Avalonia.Contracts.Services;

namespace Paarde.Avalonia.Tests.Fakes;

public class FakeHeadroomPromptSnoozeStore : IHeadroomPromptSnoozeStore
{
    private readonly HashSet<HeadroomPromptKind> _snoozed = new();

    public List<HeadroomPromptKind> SnoozeCalls { get; } = new();

    public Task<bool> IsSnoozedAsync(HeadroomPromptKind kind) => Task.FromResult(_snoozed.Contains(kind));

    public Task SnoozeAsync(HeadroomPromptKind kind)
    {
        SnoozeCalls.Add(kind);
        _snoozed.Add(kind);
        return Task.CompletedTask;
    }
}
