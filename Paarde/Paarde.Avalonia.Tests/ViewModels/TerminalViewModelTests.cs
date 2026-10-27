using Paarde.Avalonia.Services;
using Paarde.Avalonia.Tests.Fakes;
using Paarde.Avalonia.ViewModels;

namespace Paarde.Avalonia.Tests.ViewModels;

public class TerminalViewModelTests
{
    private readonly FakePtyProcessManager _ptyProcessManager = new();
    private readonly FakeLocalSettingsService _localSettingsService = new();

    private TerminalViewModel CreateSut() => new(_ptyProcessManager, _localSettingsService);

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

    [Fact]
    public void OnNavigatedFrom_UnsubscribesFromThePtyProcessManager()
    {
        var sut = CreateSut();

        sut.OnNavigatedFrom();
        _ptyProcessManager.RaiseExited(0);

        Assert.False(sut.IsRunning);
        Assert.Equal(string.Empty, sut.StatusMessage);
    }

    // TerminalViewModel.OnProcessOutputReceived/OnProcessExited marshal through
    // Dispatcher.UIThread.Post, same as RunPanelViewModel's own handlers (see that class's test
    // comment) - not exercised here since nothing pumps Dispatcher.UIThread in this plain xunit
    // host. OutputBuffer is likewise not asserted against directly here for the same reason.
}
