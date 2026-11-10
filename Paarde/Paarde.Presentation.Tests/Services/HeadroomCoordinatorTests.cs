using Paarde.Presentation.Models;

using Paarde.Presentation.Tests.Fakes;
using Paarde.Core.Models;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Services;

namespace Paarde.Presentation.Tests.Services;

public class HeadroomCoordinatorTests
{
    private readonly FakeHeadroomService _headroomService = new();
    private readonly FakeDialogService _dialogService = new();
    private readonly FakeHeadroomPromptSnoozeStore _promptSnoozeStore = new();

    private HeadroomCoordinator CreateSut() => new(_headroomService, _dialogService, _promptSnoozeStore);

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenInstalledHealthyAndWired_ReturnsSuccessWithNoPrompts()
    {
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(NotificationSeverity.Success, severity);
        Assert.Contains("healthy", message);
        Assert.Contains("already has durable", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotInstalledAndInstallAccepted_InstallsAndContinues()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(1, _headroomService.InstallCallCount);
        Assert.Equal(NotificationSeverity.Success, severity);
        Assert.Contains("Installed Headroom.", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotInstalledAndInstallDeclined_ReturnsWarningWithoutFurtherChecks()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(0, _headroomService.InstallCallCount);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.Contains("skipped installation", message);
        Assert.Empty(_headroomService.InitClaudeProjectPaths);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenUvUnavailable_ReturnsWarningWithoutPrompting()
    {
        _headroomService.IsInstalledResult = false;
        _headroomService.IsUvAvailableResult = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.Contains("uv isn't available", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotWiredAndAccepted_RunsInitClaude()
    {
        _headroomService.IsProjectWiredResult = false;
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(["/some/project"], _headroomService.InitClaudeProjectPaths);
        Assert.Equal(NotificationSeverity.Success, severity);
        Assert.Contains("now wired", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotWiredAndDeclined_SkipsWithoutRunningInitClaude()
    {
        _headroomService.IsProjectWiredResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Empty(_headroomService.InitClaudeProjectPaths);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.Contains("Skipped wiring", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenHealthHasWarnings_IncludesCheckDetailsInMessage()
    {
        _headroomService.HealthResult = new HeadroomHealthResult(1, [
            new HeadroomCheckItem("proxy", "pass", "running"),
            new HeadroomCheckItem("budget", "warn", "no budget configured - spend is unlimited"),
        ]);
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(NotificationSeverity.Informational, severity);
        Assert.Contains("budget: no budget configured", message);
        Assert.DoesNotContain("proxy: running", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenHealthWarningHasHint_IncludesHintInMessage()
    {
        _headroomService.HealthResult = new HeadroomHealthResult(1, [
            new HeadroomCheckItem("codex", "warn", "not routed (no ~/.codex/config.toml)", "wrap it: headroom wrap codex"),
        ]);
        var sut = CreateSut();

        var (message, _) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Contains("codex: not routed (no ~/.codex/config.toml) (hint: wrap it: headroom wrap codex)", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenHealthHasMultipleWarnings_ListsEachOnItsOwnLine()
    {
        _headroomService.HealthResult = new HeadroomHealthResult(1, [
            new HeadroomCheckItem("codex", "warn", "not routed", "wrap it: headroom wrap codex"),
            new HeadroomCheckItem("budget", "warn", "no budget configured"),
        ]);
        var sut = CreateSut();

        var (message, _) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.DoesNotContain(" | ", message);
        Assert.Contains("  - codex: not routed (hint: wrap it: headroom wrap codex)\n  - budget: no budget configured", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenHealthDown_StillChecksProjectWiring()
    {
        _headroomService.HealthResult = new HeadroomHealthResult(2, []);
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(NotificationSeverity.Error, severity);
        Assert.Contains("appears to be down", message);
        Assert.Contains("already has durable", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_AutomaticCheck_WhenInstallDeclined_SnoozesInstallPromptOnNextAutomaticCheck()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);
        Assert.Equal(1, _dialogService.ConfirmCallCount);

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.Contains("snoozed", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_ManualCheck_IgnoresInstallPromptSnooze()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = true;
        await _promptSnoozeStore.SnoozeAsync(HeadroomPromptKind.Install);
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: false);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(1, _headroomService.InstallCallCount);
        Assert.Equal(NotificationSeverity.Success, severity);
        Assert.Contains("Installed Headroom.", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_ManualDecline_DoesNotSnoozeFutureAutomaticChecks()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: false);
        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(2, _dialogService.ConfirmCallCount);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.DoesNotContain("snoozed", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_AutomaticCheck_WhenWiringDeclined_SnoozesWiringPromptOnNextAutomaticCheck()
    {
        _headroomService.IsProjectWiredResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);
        Assert.Equal(1, _dialogService.ConfirmCallCount);

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project", isAutomaticCheck: true);

        Assert.Equal(1, _dialogService.ConfirmCallCount);
        Assert.Equal(NotificationSeverity.Warning, severity);
        Assert.Contains("snoozed", message);
        Assert.Empty(_headroomService.InitClaudeProjectPaths);
    }
}
