using Paarde.Presentation.Contracts.Services;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Presentation.Services;

// The ILlmRunner registered with DI (see App.axaml.cs) - delegates every call to whichever concrete
// runner (ClaudeCliRunner or AnthropicApiRunner, T6.3.2) the Settings page currently has selected,
// read fresh on every call the same way ClaudeCliOptionsProvider/AnthropicApiOptionsProvider read
// their own settings - so switching runners takes effect on the very next run, not after a restart.
// PromptViewModel only ever depends on ILlmRunner, so neither it nor anything else needs to change
// for this to work - exactly the seam T6.3.1's interface was designed to leave open.
public class LlmRunnerSelector : ILlmRunner
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILlmRunner _claudeCliRunner;
    private readonly ILlmRunner _anthropicApiRunner;

    public LlmRunnerSelector(ILocalSettingsService localSettingsService, ILlmRunner claudeCliRunner, ILlmRunner anthropicApiRunner)
    {
        _localSettingsService = localSettingsService;
        _claudeCliRunner = claudeCliRunner;
        _anthropicApiRunner = anthropicApiRunner;
    }

    public async Task<bool> IsAvailableAsync()
    {
        var runner = await ResolveSelectedRunner();

        return await runner.IsAvailableAsync();
    }

    public async Task<LlmRunResult> RunPromptAsync(string prompt, string? workingDirectory = null, Action<string>? onTextDelta = null, Action<string>? onToolActivity = null, string? resumeSessionId = null, CancellationToken cancellationToken = default)
    {
        var runner = await ResolveSelectedRunner();

        return await runner.RunPromptAsync(prompt, workingDirectory, onTextDelta, onToolActivity, resumeSessionId, cancellationToken);
    }

    private async Task<ILlmRunner> ResolveSelectedRunner()
    {
        var saved = await _localSettingsService.ReadSettingAsync<string>(LlmRunnerSettingsKeys.SelectedRunner);
        var kind = Enum.TryParse<LlmRunnerKind>(saved, out var parsed) ? parsed : LlmRunnerKind.ClaudeCli;

        return kind == LlmRunnerKind.AnthropicApi ? _anthropicApiRunner : _claudeCliRunner;
    }
}
