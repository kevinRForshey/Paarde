namespace Paarde.Presentation.Contracts.Services;

// Separate from ILocalSettingsService (plain LocalSettings.json) for secrets like the Anthropic API
// key (T6.3.2) that shouldn't sit in that file as plaintext.
public interface ISecureStorageService
{
    Task<string?> ReadAsync(string key);

    Task SaveAsync(string key, string value);
}
