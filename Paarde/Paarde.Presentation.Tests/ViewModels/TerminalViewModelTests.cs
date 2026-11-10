using Paarde.Presentation.Models;
using Paarde.Presentation.Tests.Fakes;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation.Tests.ViewModels;

public class TerminalViewModelTests
{
    private readonly FakePtyProcessManager _ptyProcessManager = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();
    private readonly FakeUiDispatcher _uiDispatcher = new();

    private TerminalViewModel CreateSut() => new(_ptyProcessManager, _localSettingsService, _uiDispatcher);

    private async Task SetSavedWorkingDirectoryAsync(string path) =>
        await _localSettingsService.SaveSettingAsync(WorkingDirectorySettingsKeys.WorkingDirectory, path);

    [Fact]
    public async Task OnNavigatedTo_LoadsTheSavedWorkingDirectory()
    {
        await SetSavedWorkingDirectoryAsync("/home/user/project");
        var sut = CreateSut();

        sut.OnNavigatedTo(null!);

        Assert.Equal("/home/user/project", sut.WorkingDirectoryPath);
    }

    [Fact]
    public void RunCommandCommand_BlankCommandText_CannotExecute()
    {
        var sut = CreateSut();
        sut.CommandText = string.Empty;

        Assert.False(sut.RunCommandCommand.CanExecute(null));
    }

    [Fact]
    public void RunCommandCommand_NonBlankCommandText_CanExecute()
    {
        var sut = CreateSut();
        sut.CommandText = "ls";

        Assert.True(sut.RunCommandCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunCommandAsync_RunsTheTypedCommandInTheSavedWorkingDirectory()
    {
        await SetSavedWorkingDirectoryAsync("/home/user/project");
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.CommandText = "ls";

        await sut.RunCommandCommand.ExecuteAsync(null);

        Assert.Single(_ptyProcessManager.StartCalls);
        Assert.Equal("ls", _ptyProcessManager.StartCalls[0].Command);
        Assert.Equal("/home/user/project", _ptyProcessManager.StartCalls[0].WorkingDirectoryPath);
        Assert.True(sut.IsRunning);
    }

    [Fact]
    public async Task RunCommandAsync_NoWorkingDirectory_SetsStatusMessageAndDoesNotStart()
    {
        var sut = CreateSut();
        sut.CommandText = "ls";

        await sut.RunCommandCommand.ExecuteAsync(null);

        Assert.Empty(_ptyProcessManager.StartCalls);
        Assert.NotEmpty(sut.StatusMessage);
        Assert.Equal(NotificationSeverity.Warning, sut.StatusSeverity);
    }

    [Fact]
    public async Task CancelCommandCommand_WhileRunning_CanExecuteAndStopsTheProcess()
    {
        await SetSavedWorkingDirectoryAsync("/home/user/project");
        var sut = CreateSut();
        sut.OnNavigatedTo(null!);
        sut.CommandText = "ls";
        await sut.RunCommandCommand.ExecuteAsync(null);

        Assert.True(sut.CancelCommandCommand.CanExecute(null));
        sut.CancelCommandCommand.Execute(null);

        Assert.Equal(1, _ptyProcessManager.StopCallCount);
    }

    [Fact]
    public void CancelCommandCommand_NoRunInProgress_CannotExecute()
    {
        var sut = CreateSut();

        Assert.False(sut.CancelCommandCommand.CanExecute(null));
    }

    // OnNavigatedFrom used to unsubscribe from the PTY process
    // manager here (this ViewModel was AddTransient, one instance per page visit); now that it's
    // a DI singleton shared with the workbench dock's Terminal tab, it's a deliberate no-op, since
    // leaving the page must not stop the process's output from updating state that tab (or a
    // later revisit of this page) still needs. No dedicated test for the no-op itself: the only
    // way the old test observed unsubscription was that OnProcessExited's handler never ran at
    // all, which was verifiable synchronously (no handler call, no Dispatcher pump needed) - with
    // the handler now always invoked, its *effects* are deferred through Dispatcher.UIThread.Post
    // like every other PTY callback in this codebase, so "still subscribed" and "unsubscribed"
    // are no longer distinguishable without a dispatcher pump this plain xunit host doesn't have.
    [Fact]
    public void OnNavigatedFrom_DoesNotThrow()
    {
        var sut = CreateSut();

        sut.OnNavigatedFrom();
    }

    [Theory]
    [InlineData(-1073741510)] // STATUS_CONTROL_C_EXIT on Windows
    [InlineData(130)] // 128 + SIGINT from sh
    public void ProcessExited_StoppedWithCtrlC_ReportsItAsInformationalNotAnError(int exitCode)
    {
        var sut = CreateSut();

        _ptyProcessManager.RaiseExited(exitCode);
        _uiDispatcher.RunPending();

        Assert.Equal("Stopped with Ctrl+C.", sut.StatusMessage);
        Assert.Equal(NotificationSeverity.Informational, sut.StatusSeverity);
        Assert.False(sut.IsRunning);
    }

    [Fact]
    public void ProcessExited_NonZeroCode_ReportsAnError()
    {
        var sut = CreateSut();

        _ptyProcessManager.RaiseExited(1);
        _uiDispatcher.RunPending();

        Assert.Equal("Process exited (code 1).", sut.StatusMessage);
        Assert.Equal(NotificationSeverity.Error, sut.StatusSeverity);
    }
}
