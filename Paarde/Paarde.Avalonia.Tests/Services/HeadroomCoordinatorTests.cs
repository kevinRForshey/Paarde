using FluentAvalonia.UI.Controls;

using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Core.Models;

namespace Paarde.Avalonia.Tests.Services;

public class HeadroomCoordinatorTests
{
    private readonly FakeHeadroomService _headroomService = new();
    private readonly FakeDialogService _dialogService = new();

    private HeadroomCoordinator CreateSut() => new(_headroomService, _dialogService);

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenInstalledHealthyAndWired_ReturnsSuccessWithNoPrompts()
    {
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(InfoBarSeverity.Success, severity);
        Assert.Contains("healthy", message);
        Assert.Contains("already has durable", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotInstalledAndInstallAccepted_InstallsAndContinues()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(1, _headroomService.InstallCallCount);
        Assert.Equal(InfoBarSeverity.Success, severity);
        Assert.Contains("Installed Headroom.", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotInstalledAndInstallDeclined_ReturnsWarningWithoutFurtherChecks()
    {
        _headroomService.IsInstalledResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(0, _headroomService.InstallCallCount);
        Assert.Equal(InfoBarSeverity.Warning, severity);
        Assert.Contains("skipped installation", message);
        Assert.Empty(_headroomService.InitClaudeProjectPaths);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenUvUnavailable_ReturnsWarningWithoutPrompting()
    {
        _headroomService.IsInstalledResult = false;
        _headroomService.IsUvAvailableResult = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(0, _dialogService.ConfirmCallCount);
        Assert.Equal(InfoBarSeverity.Warning, severity);
        Assert.Contains("uv isn't available", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotWiredAndAccepted_RunsInitClaude()
    {
        _headroomService.IsProjectWiredResult = false;
        _dialogService.ConfirmResultToReturn = true;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(["/some/project"], _headroomService.InitClaudeProjectPaths);
        Assert.Equal(InfoBarSeverity.Success, severity);
        Assert.Contains("now wired", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenNotWiredAndDeclined_SkipsWithoutRunningInitClaude()
    {
        _headroomService.IsProjectWiredResult = false;
        _dialogService.ConfirmResultToReturn = false;
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Empty(_headroomService.InitClaudeProjectPaths);
        Assert.Equal(InfoBarSeverity.Warning, severity);
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

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(InfoBarSeverity.Informational, severity);
        Assert.Contains("budget: no budget configured", message);
        Assert.DoesNotContain("proxy: running", message);
    }

    [Fact]
    public async Task CheckAndOfferFixesAsync_WhenHealthDown_StillChecksProjectWiring()
    {
        _headroomService.HealthResult = new HeadroomHealthResult(2, []);
        var sut = CreateSut();

        var (message, severity) = await sut.CheckAndOfferFixesAsync("/some/project");

        Assert.Equal(InfoBarSeverity.Error, severity);
        Assert.Contains("appears to be down", message);
        Assert.Contains("already has durable", message);
    }
}
