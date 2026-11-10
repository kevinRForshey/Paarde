using Paarde.Presentation.Contracts.Services;

namespace Paarde.Presentation.Tests.Fakes;

public class FakeLocalSettingsService : ILocalSettingsService
{
    private readonly Dictionary<string, object> _values = new();

    public Task<T?> ReadSettingAsync<T>(string key) =>
        Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);

    public Task SaveSettingAsync<T>(string key, T value)
    {
        _values[key] = value!;
        return Task.CompletedTask;
    }
}
