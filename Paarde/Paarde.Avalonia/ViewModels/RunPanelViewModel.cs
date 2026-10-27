using System.Collections.ObjectModel;

using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

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
    private readonly RunConfigurationsProvider _runConfigurationsProvider;
    private readonly IBuildTestRunner _buildTestRunner;
    private readonly IRunProcessManager _runProcessManager;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<RunPanelViewModel> _logger;

    private string _workingDirectoryPath = string.Empty;

    public ObservableCollection<RunConfiguration> Configurations { get; } = [];

    [ObservableProperty]
    private RunConfiguration? selectedConfiguration;

    [ObservableProperty]
    private string outputText = string.Empty;

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
        IRunProcessManager runProcessManager,
        ILocalSettingsService localSettingsService,
        IWorkingDirectoryContext workingDirectoryContext,
        ILogger<RunPanelViewModel>? logger = null)
    {
        _runConfigurationsProvider = runConfigurationsProvider;
        _buildTestRunner = buildTestRunner;
        _runProcessManager = runProcessManager;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<RunPanelViewModel>.Instance;

        workingDirectoryContext.Changed += OnWorkingDirectoryChanged;
        _runProcessManager.OutputReceived += OnProcessOutputReceived;
        _runProcessManager.Exited += OnProcessExited;

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

        OutputText = string.Empty;
        IsOutputExpanded = true;
        StatusMessage = string.Format("RunPanel_Running".Resolve(), configuration.Name);

        try
        {
            // R4.5: Start() stops whatever's already active first, so re-pressing Run while a
            // previous session is live restarts it rather than erroring.
            _runProcessManager.Start(configuration.RunCommand, configuration.WorkingDirectoryPath);
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
        OutputText = string.Empty;
        StatusMessage = string.Format("RunPanel_Building".Resolve(), configuration.Name);

        try
        {
            var result = await _buildTestRunner.RunAsync(configuration.BuildCommand, configuration.WorkingDirectoryPath);

            OutputText = CombineOutput(result.StandardOutput, result.StandardError);
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

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() => _runProcessManager.Stop();

    private bool CanStop() => IsRunning;

    // T16.1.3-style: fires on a background thread as the process streams output, so UI state is
    // only ever touched after marshaling onto the UI thread.
    private void OnProcessOutputReceived(string line) =>
        Dispatcher.UIThread.Post(() => OutputText += line + Environment.NewLine);

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
