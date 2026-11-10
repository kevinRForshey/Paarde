using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;

using Microsoft.Extensions.Options;

namespace Paarde.Presentation.Services;

public class LocalSettingsService : ILocalSettingsService
{
    private const string _defaultLocalSettingsFile = "LocalSettings.json";

    private readonly IFileService _fileService;
    private readonly LocalSettingsOptions _options;

    private readonly string _applicationDataFolder;
    private readonly string _localsettingsFile;

    // Serializes loading, changing and writing the file; two saves at once (e.g. switching the
    // working directory) would otherwise write it concurrently and one fails with an IOException.
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IDictionary<string, object> _settings;

    private bool _isInitialized;

    public LocalSettingsService(IFileService fileService, IOptions<LocalSettingsOptions> options)
    {
        _fileService = fileService;
        _options = options.Value;

        _applicationDataFolder = ApplicationDataPaths.Resolve(_options.ApplicationDataFolder);
        _localsettingsFile = _options.LocalSettingsFile ?? _defaultLocalSettingsFile;

        _settings = new Dictionary<string, object>();
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _settings = await Task.Run(() => _fileService.Read<IDictionary<string, object>>(_applicationDataFolder, _localsettingsFile)) ?? new Dictionary<string, object>();

            _isInitialized = true;
        }
    }

    public async Task<T?> ReadSettingAsync<T>(string key)
    {
        object? obj;

        await _lock.WaitAsync();
        try
        {
            await InitializeAsync();
            _settings.TryGetValue(key, out obj);
        }
        finally
        {
            _lock.Release();
        }

        return obj == null ? default : await Json.ToObjectAsync<T>((string)obj);
    }

    public async Task SaveSettingAsync<T>(string key, T value)
    {
        var json = await Json.StringifyAsync(value);

        await _lock.WaitAsync();
        try
        {
            await InitializeAsync();

            _settings[key] = json;

            await Task.Run(() => _fileService.Save(_applicationDataFolder, _localsettingsFile, _settings));
        }
        finally
        {
            _lock.Release();
        }
    }
}
