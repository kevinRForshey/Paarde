using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.Tests.Services.Fakes;

namespace Paarde.Core.Tests.Services;

public class HeadroomServiceTests : IDisposable
{
    private const string HealthyJson = """{"exit_code": 0, "checks": [{"name": "proxy", "status": "pass", "summary": "running"}]}""";
    private const string WarnJson = """{"exit_code": 1, "checks": [{"name": "budget", "status": "warn", "summary": "no budget configured"}]}""";
    private const string DownJson = """{"exit_code": 2, "checks": [{"name": "proxy", "status": "warn", "summary": "proxy not running"}]}""";

    private readonly string _projectPath = Directory.CreateTempSubdirectory("PaardeHeadroomTests_").FullName;
    private readonly FakeBuildTestRunner _buildTestRunner = new();
    private readonly HeadroomService _sut;

    public HeadroomServiceTests()
    {
        _sut = new HeadroomService(_buildTestRunner);
    }

    public void Dispose() => Directory.Delete(_projectPath, recursive: true);

    [Fact]
    public async Task CheckHealthAsync_ParsesHealthyJson()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, HealthyJson, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.IsHealthy);
        Assert.False(result.IsDown);
        Assert.Single(result.Checks);
        Assert.Equal("proxy", result.Checks[0].Name);
        Assert.Equal("pass", result.Checks[0].Status);
        Assert.Equal("running", result.Checks[0].Summary);
    }

    [Fact]
    public async Task CheckHealthAsync_ParsesHint_WhenPresent()
    {
        const string json = """{"exit_code": 1, "checks": [{"name": "codex", "status": "warn", "summary": "not routed", "hint": "wrap it: headroom wrap codex"}]}""";
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, json, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Equal("wrap it: headroom wrap codex", result.Checks[0].Hint);
    }

    [Fact]
    public async Task CheckHealthAsync_ParsesHint_AsNull_WhenAbsentOrNull()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, HealthyJson, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Null(result.Checks[0].Hint);
    }

    [Fact]
    public async Task CheckHealthAsync_ParsesWarnJson_NotDown()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, WarnJson, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.IsHealthy);
        Assert.False(result.IsDown);
    }

    [Fact]
    public async Task CheckHealthAsync_ParsesDownJson_IsDown()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(2, DownJson, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.True(result.IsDown);
    }

    [Fact]
    public async Task CheckHealthAsync_UnparseableOutput_ReturnsNullExitCodeAndIsDown()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, "not json", string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Null(result.ExitCode);
        Assert.True(result.IsDown);
        Assert.Empty(result.Checks);
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyOutput_ReturnsNullExitCode()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(1, string.Empty, string.Empty, TimeSpan.Zero));

        var result = await _sut.CheckHealthAsync();

        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task CheckHealthAsync_RunsExpectedCommand()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, HealthyJson, string.Empty, TimeSpan.Zero));

        await _sut.CheckHealthAsync();

        // Not an exact match: CheckHealthAsync resolves "headroom" to its full path when it can
        // be found (either on PATH or in the ~/.local/bin fallback), which varies by machine -
        // only the subcommand is guaranteed to be stable across environments.
        Assert.EndsWith("doctor --json", _buildTestRunner.Calls[0].Command);
        Assert.Contains("headroom", _buildTestRunner.Calls[0].Command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsProjectWiredAsync_NoSettingsFiles_ReturnsFalse()
    {
        Assert.False(await _sut.IsProjectWiredAsync(_projectPath));
    }

    [Fact]
    public async Task IsProjectWiredAsync_LocalSettingsHasWrapMarker_ReturnsTrue()
    {
        WriteSettings(Path.Combine(_projectPath, ".claude", "settings.local.json"), wired: true);

        Assert.True(await _sut.IsProjectWiredAsync(_projectPath));
    }

    [Fact]
    public async Task IsProjectWiredAsync_SharedSettingsHasWrapMarker_ReturnsTrue()
    {
        WriteSettings(Path.Combine(_projectPath, ".claude", "settings.json"), wired: true);

        Assert.True(await _sut.IsProjectWiredAsync(_projectPath));
    }

    [Fact]
    public async Task IsProjectWiredAsync_SettingsFileWithoutMarker_ReturnsFalse()
    {
        WriteSettings(Path.Combine(_projectPath, ".claude", "settings.local.json"), wired: false);

        Assert.False(await _sut.IsProjectWiredAsync(_projectPath));
    }

    [Fact]
    public async Task IsProjectWiredAsync_MalformedJson_ReturnsFalse()
    {
        var path = Path.Combine(_projectPath, ".claude", "settings.local.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not valid json");

        Assert.False(await _sut.IsProjectWiredAsync(_projectPath));
    }

    [Fact]
    public async Task InstallAsync_RunsUvToolInstallWithExpectedPackage()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero));

        var result = await _sut.InstallAsync();

        // uv is resolved to its full path when found (same environment-dependence as
        // CheckHealthAsync_RunsExpectedCommand above), so only the subcommand/package are
        // asserted on, not an exact command string.
        var command = _buildTestRunner.Calls[0].Command;
        Assert.Contains("uv", command, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool install", command);
        Assert.Contains("headroom-ai[all]", command);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task RunInitClaudeAsync_RunsExpectedCommandInProjectDirectory()
    {
        _buildTestRunner.ResultsToReturn.Enqueue(new CommandResult(0, string.Empty, string.Empty, TimeSpan.Zero));

        await _sut.RunInitClaudeAsync(_projectPath);

        Assert.EndsWith("init claude", _buildTestRunner.Calls[0].Command);
        Assert.Contains("headroom", _buildTestRunner.Calls[0].Command, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(_projectPath, _buildTestRunner.Calls[0].WorkingDirectoryPath);
    }

    private static void WriteSettings(string path, bool wired)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var command = wired ? "/x/headroom init hook ensure --profile init-proj-abc123 --marker headroom-init-claude" : "/x/some-other-hook";
        var json = """{"hooks": {"SessionStart": [{"matcher": "startup|resume", "hooks": [{"type": "command", "command": "COMMAND_PLACEHOLDER", "timeout": 10}]}]}}"""
            .Replace("COMMAND_PLACEHOLDER", command);

        File.WriteAllText(path, json);
    }
}
