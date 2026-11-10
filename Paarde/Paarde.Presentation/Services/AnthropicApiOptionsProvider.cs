using Paarde.Presentation.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Services;

// Mirrors ClaudeCliOptionsProvider: reads fresh on every call rather than caching, so a change made
// on the Settings page takes effect on the next run, and bridges Avalonia's settings storage
// (ILocalSettingsService/ISecureStorageService) into the Func<Task<AnthropicApiOptions>>
// AnthropicApiRunner's constructor takes, since Core can't reference either service directly.
public class AnthropicApiOptionsProvider
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISecureStorageService _secureStorageService;

    public AnthropicApiOptionsProvider(ILocalSettingsService localSettingsService, ISecureStorageService secureStorageService)
    {
        _localSettingsService = localSettingsService;
        _secureStorageService = secureStorageService;
    }

    public async Task<AnthropicApiOptions> GetOptionsAsync()
    {
        var apiKey = await _secureStorageService.ReadAsync(AnthropicApiSettingsKeys.ApiKey);
        var model = await _localSettingsService.ReadSettingAsync<string>(AnthropicApiSettingsKeys.Model);
        var agentToolsEnabled = await _localSettingsService.ReadSettingAsync<bool?>(ClaudeCliSettingsKeys.AgentToolsEnabled) ?? false;

        return new AnthropicApiOptions(
            apiKey ?? string.Empty,
            string.IsNullOrWhiteSpace(model) ? AnthropicApiOptions.Default.Model : model,
            agentToolsEnabled);
    }
}
