using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeSecureStorageService : ISecureStorageService
{
    private readonly Dictionary<string, string> _values = new();

    public Task<string?> ReadAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

    public Task SaveAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }
}
