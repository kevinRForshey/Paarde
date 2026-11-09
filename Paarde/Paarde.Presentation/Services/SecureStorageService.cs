using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Core.Contracts.Services;

using Microsoft.Extensions.Options;

namespace Paarde.Presentation.Services;

// Encrypts values at rest rather than storing them as plaintext JSON the way ILocalSettingsService
// does - meant for secrets like the Anthropic API key (T6.3.2), not general settings. On Windows
// this is real OS-level protection via DPAPI (tied to the logged-in user's credentials, the same
// mechanism Windows Credential Manager itself builds on). Elsewhere there's no equivalent BCL API -
// true Keychain/Secret Service integration would mean shelling out to `security`/`secret-tool` per
// platform, each only verifiable on its own OS - so this falls back to AES-256 with a key file kept
// alongside the data, restricted to the owning user via Unix file permissions. That's meaningfully
// better than plaintext (protects against casual disk browsing, a synced/backed-up AppData folder,
// etc.) but not a hardened secret store; revisit if real OS-keychain integration becomes worth the
// per-platform testing cost.
public class SecureStorageService : ISecureStorageService
{
    private const string SecureSettingsFileName = "SecureSettings.dat";
    private const string KeyFileName = "SecureSettings.key";

    private readonly IFileService _fileService;
    private readonly string _applicationDataFolder;

    private IDictionary<string, string> _values = new Dictionary<string, string>();
    private bool _isInitialized;

    public SecureStorageService(IFileService fileService, IOptions<LocalSettingsOptions> options)
    {
        _fileService = fileService;

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _applicationDataFolder = Path.Combine(localApplicationData, options.Value.ApplicationDataFolder ?? "Paarde.Avalonia/ApplicationData");
    }

    public async Task<string?> ReadAsync(string key)
    {
        await InitializeAsync();

        if (!_values.TryGetValue(key, out var ciphertext))
        {
            return null;
        }

        return Decrypt(Convert.FromBase64String(ciphertext));
    }

    public async Task SaveAsync(string key, string value)
    {
        await InitializeAsync();

        _values[key] = Convert.ToBase64String(Encrypt(value));

        await Task.Run(() => _fileService.Save(_applicationDataFolder, SecureSettingsFileName, _values));
    }

    private async Task InitializeAsync()
    {
        if (!_isInitialized)
        {
            _values = await Task.Run(() => _fileService.Read<IDictionary<string, string>>(_applicationDataFolder, SecureSettingsFileName)) ?? new Dictionary<string, string>();
            _isInitialized = true;
        }
    }

    // AES-GCM rather than plain AES-CBC: CBC has no integrity check of its own, so a corrupted or
    // tampered ciphertext either throws a confusing padding error or - if the corruption happens to
    // land on a byte that still produces valid padding - silently decrypts to garbage instead of
    // failing. GCM's tag authenticates the ciphertext, so tampering is always detected as a clean
    // CryptographicException (see DecryptAsync_TamperedCiphertext_ThrowsRatherThanReturningGarbage).
    private static readonly int NonceSizeBytes = AesGcm.NonceByteSizes.MaxSize;
    private static readonly int TagSizeBytes = AesGcm.TagByteSizes.MaxSize;

    private byte[] Encrypt(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }

        using var aes = new AesGcm(GetOrCreateLocalKey(), TagSizeBytes);

        // Nonce doesn't need to be secret, just unique per encryption under this key - prepending it
        // (together with the tag) to the ciphertext it belongs to avoids storing it anywhere separate.
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        aes.Encrypt(nonce, plainBytes, ciphertext, tag);

        return [.. nonce, .. tag, .. ciphertext];
    }

    private string Decrypt(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser));
        }

        var nonce = data[..NonceSizeBytes];
        var tag = data[NonceSizeBytes..(NonceSizeBytes + TagSizeBytes)];
        var ciphertext = data[(NonceSizeBytes + TagSizeBytes)..];
        var plainBytes = new byte[ciphertext.Length];

        using var aes = new AesGcm(GetOrCreateLocalKey(), TagSizeBytes);
        aes.Decrypt(nonce, ciphertext, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private byte[] GetOrCreateLocalKey()
    {
        var keyPath = Path.Combine(_applicationDataFolder, KeyFileName);

        if (File.Exists(keyPath))
        {
            return Convert.FromBase64String(File.ReadAllText(keyPath));
        }

        if (!Directory.Exists(_applicationDataFolder))
        {
            Directory.CreateDirectory(_applicationDataFolder);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(key));

        // Owning user only - the whole point of restricting this file, since anyone who can read it
        // can decrypt everything SaveAsync has ever written. SetUnixFileMode is a no-op on Windows
        // (which doesn't reach this code path anyway, see Encrypt/Decrypt above).
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return key;
    }
}
