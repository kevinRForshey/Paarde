using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

// Runs against the real, installed `headroom` CLI instead of a fake IBuildTestRunner. This
// exists specifically to catch CLI output drift that HeadroomServiceTests' fakes cannot: twice
// in this codebase's history, the marker string IsProjectWiredAsync checks for was wrong because
// it was guessed rather than verified, and the fakes encoded the same wrong guess as the
// production code, so the unit test suite stayed green while real projects were misdetected.
// Every test here no-ops (not xUnit Skip - see below) when `headroom` isn't on this machine, so
// CI/sandboxes without it still pass; this suite only adds value on a machine that has the real
// CLI installed, which is also the only place it can catch real drift.
//
// xUnit 2.x's [Fact(Skip = ...)] requires a compile-time constant, so a runtime-conditional skip
// isn't available without an extra test framework dependency - an early return is the
// zero-dependency equivalent.
public class HeadroomServiceIntegrationTests : IDisposable
{
    private readonly string _projectPath = Directory.CreateTempSubdirectory("PaardeHeadroomIntegrationTests_").FullName;
    private readonly HeadroomService _sut = new(new ProcessCommandRunner());

    public void Dispose() => Directory.Delete(_projectPath, recursive: true);

    [Fact]
    public async Task CheckHealthAsync_RealCli_ProducesOutputThisServiceCanParse()
    {
        if (!_sut.IsInstalled())
        {
            return;
        }

        var result = await _sut.CheckHealthAsync();

        // A parse failure (unparseable JSON, renamed/removed fields) silently collapses to
        // ExitCode == null, IsDown == true - the same "down" fallback a genuinely dead proxy
        // produces. Asserting ExitCode.HasValue distinguishes "doctor --json's shape changed
        // under us" from "the proxy is actually down", which a fake-based test cannot do.
        Assert.True(result.ExitCode.HasValue, "CheckHealthAsync could not parse real `headroom doctor --json` output - the CLI's JSON shape may have changed.");
        Assert.NotEmpty(result.Checks);
        Assert.All(result.Checks, check =>
        {
            Assert.False(string.IsNullOrEmpty(check.Name));
            Assert.False(string.IsNullOrEmpty(check.Status));
        });
    }

    [Fact]
    public async Task RunInitClaudeAsync_RealCli_WritesHookThatIsProjectWiredAsyncRecognizes()
    {
        if (!_sut.IsInstalled())
        {
            return;
        }

        var result = await _sut.RunInitClaudeAsync(_projectPath);

        Assert.True(result.Succeeded, $"`headroom init claude` failed (exit code {result.ExitCode}): {result.StandardError}");

        // This is the exact assertion that would have caught both marker-string regressions this
        // session: IsProjectWiredAsync must recognize whatever `headroom init claude` actually
        // just wrote, not whatever we assumed it writes.
        Assert.True(await _sut.IsProjectWiredAsync(_projectPath), "IsProjectWiredAsync did not recognize the hook that the real `headroom init claude` just wrote - its marker string is out of sync with the installed CLI.");
    }
}
