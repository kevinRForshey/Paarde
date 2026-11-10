using Paarde.Presentation.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Services;

// Reads the Settings page's CLI path/arguments fresh on every call rather than caching, so
// a change made there takes effect on the very next run without needing to revisit the Prompt page.
// This is the seam between Avalonia's settings storage and ClaudeCliRunner (Core, which has no
// reference to ILocalSettingsService) - registered as the Func<Task<ClaudeCliOptions>> that
// ClaudeCliRunner's constructor takes.
public class ClaudeCliOptionsProvider
{
    private readonly ILocalSettingsService _localSettingsService;

    public ClaudeCliOptionsProvider(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task<ClaudeCliOptions> GetOptionsAsync()
    {
        var executablePath = await _localSettingsService.ReadSettingAsync<string>(ClaudeCliSettingsKeys.ExecutablePath);
        var arguments = await _localSettingsService.ReadSettingAsync<string>(ClaudeCliSettingsKeys.Arguments);
        var agentToolsEnabled = await _localSettingsService.ReadSettingAsync<bool?>(ClaudeCliSettingsKeys.AgentToolsEnabled) ?? false;

        return new ClaudeCliOptions(
            string.IsNullOrWhiteSpace(executablePath) ? ClaudeCliOptions.Default.ExecutablePath : executablePath,
            string.IsNullOrWhiteSpace(arguments) ? ClaudeCliOptions.Default.Arguments : arguments,
            agentToolsEnabled);
    }
}
