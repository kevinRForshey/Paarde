using Paarde.Avalonia.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Services;

// Same seam as ClaudeCliOptionsProvider/Epic 17's LspOptionsProvider: reads Settings fresh on
// every call rather than caching, so a change made there takes effect on the next StartAllAsync
// without needing an app restart.
public class LanguageServerOptionsProvider
{
    private readonly ILocalSettingsService _localSettingsService;

    public LanguageServerOptionsProvider(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task<IReadOnlyList<LanguageServerOptions>> GetOptionsAsync()
    {
        var saved = await _localSettingsService.ReadSettingAsync<List<LanguageServerOptions>>(LanguageServerSettingsKeys.Collection);
        if (saved is { Count: > 0 })
        {
            return saved;
        }

        return await MigrateFromLegacyOptionsAsync();
    }

    public Task SaveOptionsAsync(IReadOnlyList<LanguageServerOptions> options) =>
        _localSettingsService.SaveSettingAsync(LanguageServerSettingsKeys.Collection, options.ToList());

    // specs/multi-language-support/requirements.md R1.3: a user who already customized Epic 17's
    // single global C# executable/arguments keeps that customization instead of silently reverting
    // to "csharp-ls" the first time this per-language collection is read - folded into the new
    // defaults exactly once; the legacy keys themselves are never written to again after this.
    private async Task<IReadOnlyList<LanguageServerOptions>> MigrateFromLegacyOptionsAsync()
    {
        var legacyExecutablePath = await _localSettingsService.ReadSettingAsync<string>(LanguageServerSettingsKeys.LegacyExecutablePath);

        if (string.IsNullOrWhiteSpace(legacyExecutablePath))
        {
            return LanguageServerOptions.Defaults;
        }

        var legacyArguments = await _localSettingsService.ReadSettingAsync<string>(LanguageServerSettingsKeys.LegacyArguments) ?? string.Empty;

        return LanguageServerOptions.Defaults
            .Select(option => option.LanguageId == "csharp" ? option with { ExecutablePath = legacyExecutablePath, Arguments = legacyArguments } : option)
            .ToList();
    }
}
