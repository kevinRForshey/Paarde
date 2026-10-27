using System.Text;

using Avalonia.Threading;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Services;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Paarde.Avalonia.ViewModels;

// T16.1.2: the integrated terminal - a "run a command, stream its output" panel, reusing the
// working directory last picked on the Prompt page the same way ExplorerViewModel does (T8.1.1),
// via WorkingDirectorySettingsKeys.
// specs/terminal-emulation T7: runs the typed command through IPtyProcessManager instead of
// ITerminalCommandRunner's line-oriented streaming overload, so full-screen TUI apps typed here
// render correctly too, not just plain line-oriented commands. This is a *separate* PTY-backed
// process slot from RunPanelViewModel's (App.axaml.cs registers one keyed by
// PtyProcessManagerKey) - Run's persistent toolbar and this page are independent features, same
// as the pre-epic IRunProcessManager/ITerminalCommandRunner split; sharing RunPanelViewModel's
// singleton would mean typing a command here stops whatever Run has going, and vice versa.
public partial class TerminalViewModel : ObservableObject, INavigationAware
{
    public const string PtyProcessManagerKey = "Terminal";

    private static readonly byte[] ClearScreenSequence = Encoding.ASCII.GetBytes("\x1b[2J\x1b[H");

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

    public TerminalViewModel(
        [FromKeyedServices(PtyProcessManagerKey)] IPtyProcessManager ptyProcessManager,
        ILocalSettingsService localSettingsService,
        ILogger<TerminalViewModel>? logger = null)
    {
        _ptyProcessManager = ptyProcessManager;
        _localSettingsService = localSettingsService;
        _logger = logger ?? NullLogger<TerminalViewModel>.Instance;

        _ptyProcessManager.OutputReceived += OnProcessOutputReceived;
        _ptyProcessManager.Exited += OnProcessExited;
    }

    // T9.2.3: async void because INavigationAware.OnNavigatedTo returns void, same reasoning as
    // ExplorerViewModel.OnNavigatedTo - a corrupt settings file is logged rather than unwound
    // unobserved out of NavigationService.OnNavigated.
    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            WorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the saved working directory while navigating to the Terminal page.");
        }
    }

    // Same subscribe-in-constructor/unsubscribe-here pattern ExplorerViewModel already uses for
    // ILanguageServerSessionManager.DiagnosticsPublished - IPtyProcessManager is a singleton but
    // this ViewModel is transient (a fresh instance per navigation to the Terminal page), so
    // leaving the subscription live past this page would pile up abandoned handlers on every
    // revisit.
    public void OnNavigatedFrom()
    {
        _ptyProcessManager.OutputReceived -= OnProcessOutputReceived;
        _ptyProcessManager.Exited -= OnProcessExited;
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task RunCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkingDirectoryPath))
        {
            StatusMessage = "Terminal_NoWorkingDirectory".Resolve();
            return;
        }

        OutputBuffer.Feed(ClearScreenSequence);
        StatusMessage = string.Format("Terminal_Running".Resolve(), CommandText);

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

    // specs/terminal-emulation T6/T7: thin passthroughs so TerminalView.axaml.cs can wire
    // OutputTerminal.InputReceived/.Resized directly, same shape RunPanelViewModel already
    // exposes for ShellView.axaml.cs.
    public void WriteInput(byte[] input) => _ptyProcessManager.WriteInput(input);

    public void Resize(int cols, int rows) => _ptyProcessManager.Resize(cols, rows);

    // T16.1.3-style: fires on a background thread as the process streams output, so UI state is
    // only ever touched after marshaling onto the UI thread - same requirement as
    // PromptViewModel.OnTextDelta.
    private void OnProcessOutputReceived(byte[] bytes) =>
        Dispatcher.UIThread.Post(() => OutputBuffer.Feed(bytes));

    private void OnProcessExited(int? exitCode) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            StatusMessage = exitCode is null ? "Terminal_Stopped".Resolve() : string.Format("Terminal_Exited".Resolve(), exitCode);
        });
}
