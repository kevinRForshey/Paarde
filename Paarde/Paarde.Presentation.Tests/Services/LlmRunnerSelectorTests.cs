using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Presentation.Services;

namespace Paarde.Presentation.Tests.Services;

public class LlmRunnerSelectorTests
{
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeLlmRunner _claudeCliRunner = new();
    private readonly FakeLlmRunner _anthropicApiRunner = new();

    private LlmRunnerSelector CreateSut() => new(_localSettingsService, _claudeCliRunner, _anthropicApiRunner);

    [Fact]
    public async Task RunPromptAsync_NothingSaved_DelegatesToTheClaudeCliRunner()
    {
        var sut = CreateSut();

        await sut.RunPromptAsync("prompt");

        Assert.Equal(1, _claudeCliRunner.CallCount);
        Assert.Equal(0, _anthropicApiRunner.CallCount);
    }

    [Fact]
    public async Task RunPromptAsync_AnthropicApiSelected_DelegatesToTheAnthropicApiRunner()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.AnthropicApi));
        var sut = CreateSut();

        await sut.RunPromptAsync("prompt");

        Assert.Equal(0, _claudeCliRunner.CallCount);
        Assert.Equal(1, _anthropicApiRunner.CallCount);
    }

    [Fact]
    public async Task RunPromptAsync_ClaudeCliExplicitlySelected_DelegatesToTheClaudeCliRunner()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.ClaudeCli));
        var sut = CreateSut();

        await sut.RunPromptAsync("prompt");

        Assert.Equal(1, _claudeCliRunner.CallCount);
        Assert.Equal(0, _anthropicApiRunner.CallCount);
    }

    [Fact]
    public async Task RunPromptAsync_UnparseableSavedValue_FallsBackToTheClaudeCliRunner()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, "NotARealRunnerKind");
        var sut = CreateSut();

        await sut.RunPromptAsync("prompt");

        Assert.Equal(1, _claudeCliRunner.CallCount);
        Assert.Equal(0, _anthropicApiRunner.CallCount);
    }

    [Fact]
    public async Task RunPromptAsync_PassesAllArgumentsThroughToTheResolvedRunner()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.AnthropicApi));
        var sut = CreateSut();
        var deltas = new List<string>();
        var toolActivities = new List<string>();
        using var cts = new CancellationTokenSource();

        await sut.RunPromptAsync("prompt", "/work/dir", deltas.Add, toolActivities.Add, "session-id", cts.Token);

        Assert.Equal("prompt", _anthropicApiRunner.LastPrompt);
        Assert.Equal("/work/dir", _anthropicApiRunner.LastWorkingDirectory);
        Assert.Equal("session-id", _anthropicApiRunner.LastResumeSessionId);
        Assert.Equal(cts.Token, _anthropicApiRunner.LastCancellationToken);
    }

    [Fact]
    public async Task RunPromptAsync_ReturnsTheResolvedRunnersResult()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.AnthropicApi));
        _anthropicApiRunner.ResultToReturn = new LlmRunResult { Success = true, Output = "from the api runner" };
        var sut = CreateSut();

        var result = await sut.RunPromptAsync("prompt");

        Assert.Equal("from the api runner", result.Output);
    }

    [Fact]
    public async Task IsAvailableAsync_DelegatesToTheResolvedRunner()
    {
        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.AnthropicApi));
        _anthropicApiRunner.AvailableToReturn = false;
        _claudeCliRunner.AvailableToReturn = true;
        var sut = CreateSut();

        Assert.False(await sut.IsAvailableAsync());
    }

    [Fact]
    public async Task ResolveSelectedRunner_ReadsFreshOnEveryCallRatherThanCaching()
    {
        var sut = CreateSut();
        await sut.RunPromptAsync("first prompt");
        Assert.Equal(1, _claudeCliRunner.CallCount);

        await _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, nameof(LlmRunnerKind.AnthropicApi));
        await sut.RunPromptAsync("second prompt");

        Assert.Equal(1, _claudeCliRunner.CallCount);
        Assert.Equal(1, _anthropicApiRunner.CallCount);
    }
}
