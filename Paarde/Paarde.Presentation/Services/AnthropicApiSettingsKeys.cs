namespace Paarde.Presentation.Services;

// Shared between SettingsViewModel (where these are edited) and AnthropicApiOptionsProvider (where
// they're read to build the AnthropicApiOptions passed into each run) - same split as
// ClaudeCliSettingsKeys. ApiKey is read/written through ISecureStorageService, not
// ILocalSettingsService - Model is an ordinary, non-secret setting.
public static class AnthropicApiSettingsKeys
{
    public const string ApiKey = "AnthropicApiKey";

    public const string Model = "AnthropicApiModel";
}
