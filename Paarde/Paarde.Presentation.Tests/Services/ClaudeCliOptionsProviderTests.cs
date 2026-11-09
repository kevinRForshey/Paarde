using Paarde.Presentation.Tests.Fakes;
using Paarde.Presentation.Services;

namespace Paarde.Presentation.Tests.Services;

public class ClaudeCliOptionsProviderTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();

    private ClaudeCliOptionsProvider CreateSut() => new(_localSettingsService);

    [Fact]
    public async Task GetOptionsAsync_NoSavedSettings_ReturnsDefaults()
    {
        var sut = CreateSut();

        var options = await sut.GetOptionsAsync();

        Assert.Equal("claude", options.ExecutablePath);
        Assert.Equal("-p", options.Arguments);
    }

    [Fact]
    public async Task GetOptionsAsync_SavedSettings_ReturnsConfiguredExecutablePathAndArguments()
    {
        await _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.ExecutablePath, "/usr/local/bin/claude");
        await _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.Arguments, "-p --model=opus");
        var sut = CreateSut();

        var options = await sut.GetOptionsAsync();

        Assert.Equal("/usr/local/bin/claude", options.ExecutablePath);
        Assert.Equal("-p --model=opus", options.Arguments);
    }
}
