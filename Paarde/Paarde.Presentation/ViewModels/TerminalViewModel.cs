using System.Text;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;

using Paarde.Presentation.Contracts.ViewModels;
using Paarde.Presentation.Services;
using Paarde.Presentation.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Presentation.ViewModels;

// The integrated terminal - a "run a command, stream its output" panel, reusing the
// working directory last picked on the Prompt page the same way ExplorerViewModel does,
// via WorkingDirectorySettingsKeys.
// Runs the typed command through IPtyProcessManager instead of
// ITerminalCommandRunner's line-oriented streaming overload, so full-screen TUI apps typed here
// render correctly too, not just plain line-oriented commands. This is a *separate* PTY-backed
// process slot from RunPanelViewModel's (App.axaml.cs registers one keyed by
// PtyProcessManagerKey) - Run's persistent toolbar and this page are independent features, same
// as the pre-epic IRunProcessManager/ITerminalCommandRunner split; sharing RunPanelViewModel's
// singleton would mean typing a command here stops whatever Run has going, and vice versa.
// Promoted from AddTransient to AddSingleton - the Terminal page
// and the workbench dock's Terminal tab now bind to the exact same instance/session rather than
// each getting an independent PTY, per design.md's decision against two terminals racing to claim
// the same tab's content.
public partial class TerminalViewModel : ObservableObject, INavigationAware
{
    public const string PtyProcessManagerKey = "Terminal";

    private static readonly byte[] ClearScreenSequence = Encoding.ASCII.GetBytes("\x1b[2J\x1b[H");

    // RIS: a full reset, not just a clear, before a new command - a TUI that was killed mid-run
    // (Cancel) leaves its colours, alternate screen and input modes set otherwise.
    private static readonly byte[] ResetTerminalSequence = Encoding.ASCII.GetBytes("\u001bc");

    private readonly IPtyProcessManager _ptyProcessManager;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ILogger<TerminalViewModel> _logger;

    [ObservableProperty]
    private string workingDirectoryPath = string.Empty;

    [ObservableProperty]
    private string commandText = string.Empty;

    public TerminalBuffer OutputBuffer { get; } = new(cols: 80, rows: 24);

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private NotificationSeverity statusSeverity = NotificationSeverity.Informational;

    private readonly IUiDispatcher _uiDispatcher;

    public TerminalViewModel(
        [FromKeyedServices(PtyProcessManagerKey)] IPtyProcessManager ptyProcessManager,
        ILocalSettingsService localSettingsService,
        IUiDispatcher uiDispatcher,
        ILogger<TerminalViewModel>? logger = null)
    {
        _uiDispatcher = uiDispatcher;
        _ptyProcessManager = ptyProcessManager;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<TerminalViewModel>.Instance;

        _ptyProcessManager.OutputReceived += OnProcessOutputReceived;
        OutputBuffer.ResponseReady += _ptyProcessManager.WriteInput;
        _ptyProcessManager.Exited += OnProcessExited;
    }

    // Async void because INavigationAware.OnNavigatedTo returns void, same reasoning as
    // ExplorerViewModel.OnNavigatedTo - a corrupt settings file is logged rather than unwound
    // unobserved out of NavigationService.OnNavigated.
    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            WorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException)
        {
            _logger.LogError(ex, "Failed to load the saved working directory while navigating to the Terminal page.");
        }
    }

    // A no-op now that this ViewModel is a DI singleton shared
    // with the workbench dock's Terminal tab - unlike the old per-page-transient lifetime this
    // subscribe-in-constructor/unsubscribe-here pattern was written for, leaving this page no
    // longer means nobody is watching: the dock tab may still be visible, and even if it isn't,
    // the running process's own output needs to keep buffering so it's there when the user comes
    // back. INavigationAware still requires the method.
    public void OnNavigatedFrom()
    {
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RunCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectoryPath))
        {
            StatusMessage = "Terminal_NoWorkingDirectory".Resolve();
            StatusSeverity = NotificationSeverity.Warning;
            return;
        }

        OutputBuffer.Feed(ResetTerminalSequence);
        StatusMessage = string.Format("Terminal_Running".Resolve(), CommandText);
        StatusSeverity = NotificationSeverity.Informational;

        try
        {
            // R4.5-equivalent semantics: StartAsync() stops whatever's already active on this
            // shared PTY slot first, so re-pressing Run while a previous command is live restarts
            // it rather than erroring.
            await _ptyProcessManager.StartAsync(CommandText, WorkingDirectoryPath, OutputBuffer.Cols, OutputBuffer.Rows);
            IsRunning = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Failed to run a terminal command in {WorkingDirectory}.", WorkingDirectoryPath);
            StatusMessage = string.Format("Terminal_RunStartError".Resolve(), ex.Message);
            StatusSeverity = NotificationSeverity.Error;
        }
    }

    private bool CanRunCommand() => !IsRunning && !string.IsNullOrWhiteSpace(CommandText);

    partial void OnCommandTextChanged(string value) => RunCommandCommand.NotifyCanExecuteChanged();

    partial void OnIsRunningChanged(bool value)
    {
        RunCommandCommand.NotifyCanExecuteChanged();
        CancelCommandCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanCancelCommand))]
    private void CancelCommand() => _ptyProcessManager.Stop();

    private bool CanCancelCommand() => IsRunning;

    [RelayCommand]
    private void ClearOutput() => OutputBuffer.Feed(ClearScreenSequence);

    // Thin passthroughs so TerminalView.axaml.cs can wire
    // OutputTerminal.InputReceived/.Resized directly, same shape RunPanelViewModel already
    // exposes for ShellView.axaml.cs.
    public void WriteInput(byte[] input) => _ptyProcessManager.WriteInput(input);

    public void Resize(int cols, int rows) => _ptyProcessManager.Resize(cols, rows);

    // T16.1.3-style: fires on a background thread as the process streams output, so UI state is
    // only ever touched after marshaling onto the UI thread - same requirement as
    // PromptViewModel.OnTextDelta.
    private void OnProcessOutputReceived(byte[] bytes) =>
        _uiDispatcher.Post(() => OutputBuffer.Feed(bytes));

    private void OnProcessExited(int? exitCode) =>
        _uiDispatcher.Post(() =>
        {
            IsRunning = false;
            StatusMessage = exitCode is null ? "Terminal_Stopped".Resolve() : string.Format("Terminal_Exited".Resolve(), exitCode);
            StatusSeverity = exitCode is null or 0 ? NotificationSeverity.Informational : NotificationSeverity.Error;
        });
}
