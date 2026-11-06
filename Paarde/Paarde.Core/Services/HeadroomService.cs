using Newtonsoft.Json.Linq;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Helpers;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// Implements IHeadroomService by shelling out to the `headroom` CLI through the existing
// IBuildTestRunner (the same ProcessStartInfo pattern ProcessCommandRunner/ClaudeCliRunner/
// GitStatusService already use), rather than hand-rolling another process-spawning path. Mirrors
// the logic already written, tested, and verified in the standalone headroom-check.sh/.ps1
// scripts (~/LinuxSetUpScripts) - this is the same checks, reimplemented as a native Paarde
// feature instead of a script someone has to know to run.
public class HeadroomService : IHeadroomService
{
    private const string WrapMarkerCommand = "headroom wrap selfheal";
    private const string HeadroomPackageSpec = "headroom-ai[all]";

    // pip --user and `uv tool install` both default to putting shims here on every platform
    // (uv documents this as a deliberate cross-platform default). A GUI app launched from a
    // desktop entry/launcher often inherits a minimal PATH that never includes this directory
    // even though an interactive shell's PATH does, so IsInstalled()/IsUvAvailable() and the
    // commands actually run below all need to fall back to checking it explicitly.
    private static readonly string LocalBinFallbackDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin");

    private readonly IBuildTestRunner _buildTestRunner;

    public HeadroomService(IBuildTestRunner buildTestRunner)
    {
        _buildTestRunner = buildTestRunner;
    }

    public bool IsInstalled() => ResolveExecutable("headroom") is not null;

    public bool IsUvAvailable() => ResolveExecutable("uv") is not null;

    public async Task<HeadroomHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var headroom = ResolveExecutable("headroom") ?? "headroom";
        var result = await _buildTestRunner.RunAsync($"\"{headroom}\" doctor --json", Environment.CurrentDirectory, cancellationToken);

        return TryParseDoctorJson(result.StandardOutput, out var health) ? health : new HeadroomHealthResult(null, []);
    }

    public async Task<bool> IsProjectWiredAsync(string projectPath)
    {
        var localSettingsPath = Path.Combine(projectPath, ".claude", "settings.local.json");
        var sharedSettingsPath = Path.Combine(projectPath, ".claude", "settings.json");

        return await IsWiredAsync(localSettingsPath) || await IsWiredAsync(sharedSettingsPath);
    }

    public Task<CommandResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        var uv = ResolveExecutable("uv") ?? "uv";

        return _buildTestRunner.RunAsync($"\"{uv}\" tool install \"{HeadroomPackageSpec}\"", Environment.CurrentDirectory, cancellationToken);
    }

    public Task<CommandResult> RunInitClaudeAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        var headroom = ResolveExecutable("headroom") ?? "headroom";

        return _buildTestRunner.RunAsync($"\"{headroom}\" init claude", projectPath, cancellationToken);
    }

    // Checks the real PATH first, then LocalBinFallbackDirectory, so detection and the commands
    // built from this match what's actually on disk even when the process's inherited PATH is
    // missing ~/.local/bin (the common GUI-launcher case this method exists to work around).
    private static string? ResolveExecutable(string name)
    {
        var onPath = ExecutablePathResolver.ResolveFullPath(name);

        return onPath ?? ExecutablePathResolver.ResolveFullPath(name, LocalBinFallbackDirectory);
    }

    // Never throws: a missing file, a directory that doesn't exist, or malformed JSON all just
    // mean "not wired" - same fail-closed-to-false posture as the bash/PowerShell scripts'
    // is_durably_wired/Test-DurablyWired.
    private static async Task<bool> IsWiredAsync(string settingsFilePath)
    {
        if (!File.Exists(settingsFilePath))
        {
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsFilePath);
            var root = JObject.Parse(json);
            var sessionStartBlocks = root["hooks"]?["SessionStart"] as JArray;

            if (sessionStartBlocks is null)
            {
                return false;
            }

            return sessionStartBlocks
                .SelectMany(block => block["hooks"] as JArray ?? [])
                .Any(hook => hook["command"]?.Value<string>()?.Contains(WrapMarkerCommand, StringComparison.Ordinal) == true);
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or IOException)
        {
            return false;
        }
    }

    // Never throws: unparseable or missing JSON comes back as a false return, which
    // CheckHealthAsync maps to HeadroomHealthResult.ExitCode == null (IsDown == true) - the same
    // fallback headroom-check.sh/.ps1 use when `headroom doctor --json` produces no usable output.
    private static bool TryParseDoctorJson(string cliOutput, out HeadroomHealthResult result)
    {
        result = new HeadroomHealthResult(null, []);

        if (string.IsNullOrWhiteSpace(cliOutput))
        {
            return false;
        }

        try
        {
            var root = JObject.Parse(cliOutput);
            var exitCode = root["exit_code"]?.Value<int>();
            var checks = (root["checks"] as JArray ?? [])
                .Select(check => new HeadroomCheckItem(
                    check["name"]?.Value<string>() ?? string.Empty,
                    check["status"]?.Value<string>() ?? string.Empty,
                    check["summary"]?.Value<string>() ?? string.Empty))
                .ToList();

            result = new HeadroomHealthResult(exitCode, checks);
            return true;
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return false;
        }
    }
}
