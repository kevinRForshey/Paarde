namespace Paarde.Avalonia.Services;

// Shared between SettingsViewModel (where these are edited) and LanguageServerOptionsProvider
// (where they're read to build each language's LanguageServerOptions) - same split
// ClaudeCliSettingsKeys/ClaudeCliOptionsProvider established (T6.1.4), now keyed by language
// instead of one flat pair.
public static class LanguageServerSettingsKeys
{
    // The whole per-language collection lives under one key (a JSON array via
    // ILocalSettingsService's generic Read/SaveSettingAsync<T>), rather than one settings key per
    // language per field - keeps the settings file open-ended for however many languages end up
    // configured, instead of baking today's three into named keys.
    public const string Collection = "LanguageServerOptionsCollection";

    // Epic 17's single global keys - read once, as a migration fallback, by
    // LanguageServerOptionsProvider when Collection has never been saved; never written to again.
    public const string LegacyExecutablePath = "LspExecutablePath";

    public const string LegacyArguments = "LspArguments";
}
