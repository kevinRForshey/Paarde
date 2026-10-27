using System.Collections.ObjectModel;
using System.Text;

using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;
using Paarde.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// Epic 20 (specs/build-and-run F20.3): a DI singleton, not a page ViewModel - it backs
// ShellView's persistent toolbar/output strip (R5.1/R5.2), so it has to survive every page
// navigation rather than being created and torn down with one page. Learns the working directory
// via IWorkingDirectoryContext (design.md's "working-directory-changed pub/sub hook") instead of
// INavigationAware.OnNavigatedTo, which this class has no equivalent lifecycle hook for.
public partial class RunPanelViewModel : ObservableObject
{
    // specs/terminal-emulation T6 (R5.1): one \x1b[2J\x1b[H (clear screen + home cursor) fed to
    // OutputBuffer in place of the old OutputText = string.Empty - there's no "clear" concept on a
    // TerminalBuffer beyond feeding it the same escape sequence a real terminal would receive.
    private static readonly byte[] ClearScreenSequence = Encoding.ASCII.GetBytes("\x1b[2J\x1b[H");

    private readonly RunConfigurationsProvider _runConfigurationsProvider;
    private readonly IBuildTestRunner _buildTestRunner;
    private readonly IPtyProcessManager _ptyProcessManager;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<RunPanelViewModel> _logger;

    private string _workingDirectoryPath = string.Empty;

    public ObservableCollection<RunConfiguration> Configurations { get; } = [];

    // specs/terminal-emulation T6 (R3.3): the strip's single TerminalBuffer, fed by both Run (via
    // IPtyProcessManager.OutputReceived) and Build (via IBuildTestRunner's plain-text result,
    // normalized through EncodeForTerminal) - ShellView.axaml binds a TerminalControl to this
    // instead of the old OutputText TextBox.
    public TerminalBuffer OutputBuffer { get; } = new(cols: 80, rows: 24);

    [ObservableProperty]
    private RunConfiguration? selectedConfiguration;

    [ObservableProperty]
    private bool isBuilding;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    // R5.2: the output strip starts collapsed (ShellView.axaml) and expands itself the moment
    // there's something worth showing (a build starting/failing, or a run starting) - design.md
    // "Shell placement".
    [ObservableProperty]
    private bool isOutputExpanded;

    public RunPanelViewModel(
        RunConfigurationsProvider runConfigurationsProvider,
        IBuildTestRunner buildTestRunner,
        IPtyProcessManager ptyProcessManager,
        ILocalSettingsService localSettingsService,
        IWorkingDirectoryContext workingDirectoryContext,
        ILogger<RunPanelViewModel>? logger = null)
    {
        _runConfigurationsProvider = runConfigurationsProvider;
        _buildTestRunner = buildTestRunner;
        _ptyProcessManager = ptyProcessManager;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<RunPanelViewModel>.Instance;

        workingDirectoryContext.Changed += OnWorkingDirectoryChanged;
        _ptyProcessManager.OutputReceived += OnProcessOutputReceived;
        _ptyProcessManager.Exited += OnProcessExited;

        // Covers the case where the user never visits the Prompt page this session - the saved
        // working directory (if any) is picked up here instead of only ever waiting for
        // IWorkingDirectoryContext.Changed to fire.
        _ = LoadSavedWorkingDirectoryAsync();
    }

    private async Task LoadSavedWorkingDirectoryAsync()
    {
        try
        {
            var savedWorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
            await SetWorkingDirectoryAsync(savedWorkingDirectoryPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the saved working directory for the Run panel.");
        }
    }

    private void OnWorkingDirectoryChanged(string workingDirectoryPath) =>
        Dispatcher.UIThread.Post(() => _ = SetWorkingDirectoryAsync(workingDirectoryPath));

    private async Task SetWorkingDirectoryAsync(string workingDirectoryPath)
    {
        if (workingDirectoryPath == _workingDirectoryPath)
        {
            return;
        }

        _workingDirectoryPath = workingDirectoryPath;
        await RefreshConfigurationsAsync();
    }

    private async Task RefreshConfigurationsAsync()
    {
        if (string.IsNullOrWhiteSpace(_workingDirectoryPath))
        {
            Configurations.Clear();
            SelectedConfiguration = null;
            return;
        }

        try
        {
            var snapshot = await _runConfigurationsProvider.GetConfigurationsAsync(_workingDirectoryPath);

            Configurations.Clear();
            foreach (var configuration in snapshot.Configurations)
            {
                Configurations.Add(configuration);
            }

            SelectedConfiguration = Configurations.FirstOrDefault(c => c.Id == snapshot.SelectedConfigurationId) ?? Configurations.FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to detect run configurations for {WorkingDirectory}.", _workingDirectoryPath);
        }
    }

    partial void OnSelectedConfigurationChanged(RunConfiguration? value)
    {
        BuildCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();

        if (value is null || string.IsNullOrWhiteSpace(_workingDirectoryPath))
        {
            return;
        }

        _ = _runConfigurationsProvider.SaveSelectedConfigurationIdAsync(_workingDirectoryPath, value.Id);
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync() => await BuildInternalAsync();

    private bool CanBuild() => !IsBuilding && SelectedConfiguration is not null;

    // R4.3: builds first by default (if a build command is configured); a build failure stops
    // the sequence before the run command ever starts, surfacing the same way a standalone Build
    // failure would (R3.2).
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var configuration = SelectedConfiguration;
        if (configuration is null)
        {
            return;
        }

        if (!await BuildInternalAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.RunCommand))
        {
            return;
        }

        OutputBuffer.Feed(ClearScreenSequence);
        IsOutputExpanded = true;
        StatusMessage = string.Format("RunPanel_Running".Resolve(), configuration.Name);

        try
        {
            // R4.5: StartAsync() stops whatever's already active first, so re-pressing Run while a
            // previous session is live restarts it rather than erroring.
            await _ptyProcessManager.StartAsync(configuration.RunCommand, configuration.WorkingDirectoryPath, OutputBuffer.Cols, OutputBuffer.Rows);
            IsRunning = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Failed to start the run command for {Configuration}.", configuration.Name);
            StatusMessage = string.Format("RunPanel_RunStartError".Resolve(), ex.Message);
        }
    }

    private bool CanRun() => !IsBuilding && SelectedConfiguration is not null;

    private async Task<bool> BuildInternalAsync()
    {
        var configuration = SelectedConfiguration;
        if (configuration is null)
        {
            StatusMessage = "RunPanel_NoConfiguration".Resolve();
            return false;
        }

        // R3.3: a blank build command (legitimate for an interpreted-language configuration)
        // skips the build step entirely rather than erroring.
        if (string.IsNullOrWhiteSpace(configuration.BuildCommand))
        {
            return true;
        }

        IsBuilding = true;
        IsOutputExpanded = true;
        OutputBuffer.Feed(ClearScreenSequence);
        StatusMessage = string.Format("RunPanel_Building".Resolve(), configuration.Name);

        try
        {
            var result = await _buildTestRunner.RunAsync(configuration.BuildCommand, configuration.WorkingDirectoryPath);

            OutputBuffer.Feed(EncodeForTerminal(CombineOutput(result.StandardOutput, result.StandardError)));
            StatusMessage = result.Succeeded
                ? string.Format("RunPanel_BuildSucceeded".Resolve(), result.Duration.TotalSeconds)
                : string.Format("RunPanel_BuildFailed".Resolve(), result.ExitCode, result.Duration.TotalSeconds);

            return result.Succeeded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Failed to run the build command for {Configuration}.", configuration.Name);
            StatusMessage = string.Format("RunPanel_BuildError".Resolve(), ex.Message);
            return false;
        }
        finally
        {
            IsBuilding = false;
        }
    }

    private static string CombineOutput(string standardOutput, string standardError) =>
        string.IsNullOrEmpty(standardError) ? standardOutput : standardOutput + Environment.NewLine + standardError;

    // specs/terminal-emulation T6: IBuildTestRunner's result comes from a plain, non-PTY Process
    // redirect, so its strings carry bare \n. XTerm.NET's Terminal treats \n strictly per VT100
    // semantics - it moves down a row without returning to column 0 - so feeding bare \n produces
    // a "staircase" (each line starting one column further right than the last), confirmed
    // empirically against the real library. IPtyProcessManager.OutputReceived bytes don't need
    // this: a real PTY's line discipline already translates outgoing \n to \r\n before the reader
    // stream sees it.
    private static byte[] EncodeForTerminal(string text) =>
        Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _ptyProcessManager.Stop();

    private bool CanStop() => IsRunning;

    // specs/terminal-emulation T6: thin passthroughs so ShellView.axaml.cs can wire
    // OutputTerminal.InputReceived/.Resized directly (the same "stay ignorant of the service"
    // shape TerminalControl itself uses for those events) without the View reaching past this
    // ViewModel to IPtyProcessManager.
    public void WriteInput(byte[] input) => _ptyProcessManager.WriteInput(input);

    public void Resize(int cols, int rows) => _ptyProcessManager.Resize(cols, rows);

    // T16.1.3-style: fires on a background thread as the process streams output, so UI state is
    // only ever touched after marshaling onto the UI thread.
    private void OnProcessOutputReceived(byte[] bytes) =>
        Dispatcher.UIThread.Post(() => OutputBuffer.Feed(bytes));

    private void OnProcessExited(int? exitCode) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            StatusMessage = exitCode is null ? "RunPanel_Stopped".Resolve() : string.Format("RunPanel_Exited".Resolve(), exitCode);
        });

    partial void OnIsBuildingChanged(bool value)
    {
        BuildCommand.NotifyCanExecuteChanged();
        RunCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => StopCommand.NotifyCanExecuteChanged();
}
