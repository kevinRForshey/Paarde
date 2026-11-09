using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Presentation.Services;

namespace Paarde.Presentation.Tests.Services;

public class AnthropicApiOptionsProviderTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeSecureStorageService _secureStorageService = new();

    private AnthropicApiOptionsProvider CreateSut() => new(_localSettingsService, _secureStorageService);

    [Fact]
    public async Task GetOptionsAsync_NothingSaved_ReturnsEmptyKeyAndTheDefaultModel()
    {
        var options = await CreateSut().GetOptionsAsync();

        Assert.Equal(string.Empty, options.ApiKey);
        Assert.Equal(AnthropicApiOptions.Default.Model, options.Model);
    }

    [Fact]
    public async Task GetOptionsAsync_ApiKeySaved_ReadsItFromSecureStorageRatherThanLocalSettings()
    {
        await _secureStorageService.SaveAsync(AnthropicApiSettingsKeys.ApiKey, "sk-ant-super-secret");

        var options = await CreateSut().GetOptionsAsync();

        Assert.Equal("sk-ant-super-secret", options.ApiKey);
    }

    [Fact]
    public async Task GetOptionsAsync_ModelSaved_ReturnsIt()
    {
        await _localSettingsService.SaveSettingAsync(AnthropicApiSettingsKeys.Model, "claude-opus-5");

        var options = await CreateSut().GetOptionsAsync();

        Assert.Equal("claude-opus-5", options.Model);
    }

    [Fact]
    public async Task GetOptionsAsync_ReadsFreshOnEveryCallRatherThanCaching()
    {
        var sut = CreateSut();
        await _secureStorageService.SaveAsync(AnthropicApiSettingsKeys.ApiKey, "first-key");
        Assert.Equal("first-key", (await sut.GetOptionsAsync()).ApiKey);

        await _secureStorageService.SaveAsync(AnthropicApiSettingsKeys.ApiKey, "second-key");

        Assert.Equal("second-key", (await sut.GetOptionsAsync()).ApiKey);
    }
}
