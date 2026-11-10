using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;

using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;

using Paarde.Presentation.Contracts.ViewModels;
using Paarde.Presentation.Services;
using Paarde.Presentation.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Presentation.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INavigationAware
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISecureStorageService _secureStorageService;
    private readonly LanguageServerOptionsProvider _languageServerOptionsProvider;
    private readonly RunConfigurationsProvider _runConfigurationsProvider;
    private readonly IHeadroomCoordinator _headroomCoordinator;
    private readonly IHeadroomStatusStore _headroomStatusStore;
    private readonly IWindowAppearanceService _windowAppearanceService;
    private readonly ILogger<SettingsViewModel> _logger;

    // The working directory Run configurations below belong
    // to - read once in OnNavigatedTo the same way TerminalViewModel reads it, not re-exposed as
    // its own editable field here since this page doesn't change the working directory, only
    // edits what's configured for whichever one is currently selected on the Prompt page.
    private string _runConfigurationsWorkingDirectoryPath = string.Empty;

    // The value loaded in OnNavigatedTo, compared against the
    // live InterfaceMode property to decide InterfaceModeChangeRequiresRestart - switching modes
    // changes which pages/startup view model the shell navigates to, which isn't something that
    // can be re-applied live the way Theme can.
    private InterfaceMode _loadedInterfaceMode;

    [ObservableProperty]
    private AppTheme _theme;

    [ObservableProperty]
    private string _versionDescription;

    [ObservableProperty]
    private string claudeCliExecutablePath = ClaudeCliOptions.Default.ExecutablePath;

    [ObservableProperty]
    private string claudeCliArguments = ClaudeCliOptions.Default.Arguments;

    // Off by default - this changes what the model can *do* mid-run (read/grep/
    // glob the project), not just how it's invoked, so it shouldn't turn on silently. Shared by
    // whichever ILlmRunner is selected.
    [ObservableProperty]
    private bool agentToolsEnabled;

    // The commands run after an apply, if non-blank, to check the result still
    // builds/passes its tests. Blank means "skip" rather than falling back to some default command
    // name - not every working directory is a .NET project, and a wrong guess shelling out to a
    // nonexistent command would be a worse default than doing nothing.
    [ObservableProperty]
    private string buildCommand = "dotnet build";

    [ObservableProperty]
    private string testCommand = "dotnet test";

    // One row per configured language, replacing Epic 17's single flat
    // LspExecutablePath/LspArguments pair - populated from LanguageServerOptionsProvider in
    // OnNavigatedTo, saved back as a whole collection on any row's edit.
    public ObservableCollection<LanguageServerOptionsRowViewModel> LanguageServerRows { get; } = [];

    // One row per Run configuration for the current working
    // directory - populated from RunConfigurationsProvider in OnNavigatedTo, saved back as a
    // whole collection on any row's edit/delete, same shape as LanguageServerRows above.
    public ObservableCollection<RunConfigurationRowViewModel> RunConfigurationRows { get; } = [];

    [ObservableProperty]
    private bool hasRunConfigurationsWorkingDirectory;

    // Which ILlmRunner implementation LlmRunnerSelector should delegate to.
    [ObservableProperty]
    private LlmRunnerKind selectedRunnerKind = LlmRunnerKind.ClaudeCli;

    // Persisted through ISecureStorageService (encrypted at rest), not ILocalSettingsService -
    // see that interface's own comment for why.
    [ObservableProperty]
    private string anthropicApiKey = string.Empty;

    [ObservableProperty]
    private string anthropicApiModel = AnthropicModelCatalog.DefaultModelId;

    // Which of the two top-level shells the app starts into.
    // Loaded in OnNavigatedTo/persisted via OnInterfaceModeChanged like SelectedRunnerKind below -
    // not a live-apply Command like SwitchThemeCommand, since this governs startup navigation and
    // ShellView's menu items, not something applicable to the already-running shell.
    [ObservableProperty]
    private InterfaceMode interfaceMode = InterfaceMode.EditorFocused;

    [ObservableProperty]
    private bool interfaceModeChangeRequiresRestart;

    // Off by default - the Mica/Acrylic window backdrop is a visual
    // preference, not something that should turn on silently (same reasoning as AgentToolsEnabled
    // above). Unlike InterfaceMode, this applies live to the already-running MainWindow instead of
    // requiring a restart - see OnIsTransparencyEnabledChanged.
    [ObservableProperty]
    private bool isTransparencyEnabled;

    // HeadroomSettingsKeys.IntegrationEnabled: off by default, same reasoning as
    // IsTransparencyEnabled - this spawns external processes and can prompt to install software,
    // so it must never activate silently on startup.
    [ObservableProperty]
    private bool isHeadroomIntegrationEnabled;

    // Reflects the most recent IHeadroomCoordinator result, whether that came from the automatic
    // startup check (via IHeadroomStatusStore) or from CheckHeadroomNowCommand below.
    [ObservableProperty]
    private string headroomStatusMessage = string.Empty;

    [ObservableProperty]
    private NotificationSeverity headroomStatusSeverity = NotificationSeverity.Informational;

    public IReadOnlyList<InterfaceMode> AvailableInterfaceModes { get; } = Enum.GetValues<InterfaceMode>();

    public IReadOnlyList<LlmRunnerKind> AvailableRunnerKinds { get; } = Enum.GetValues<LlmRunnerKind>();

    public IReadOnlyList<AnthropicModelInfo> AvailableModels { get; } = AnthropicModelCatalog.Models;

    // AnthropicApiModel is persisted as a bare model id (string), but the model picker's ComboBox
    // needs an actual AvailableModels item to bind SelectedItem to - combining Avalonia's
    // SelectedValue/SelectedValueBinding with DisplayMemberBinding throws an InvalidCastException
    // (the selection box ends up applying the item template to the raw string id instead of the
    // matched item), so this wraps the string property as the AnthropicModelInfo the ComboBox
    // actually binds to. OnAnthropicApiModelChanged below keeps it in sync when the id changes from
    // elsewhere (e.g. OnNavigatedTo restoring a saved value).
    public AnthropicModelInfo? SelectedAnthropicModel
    {
        get => AvailableModels.FirstOrDefault(model => model.Id == AnthropicApiModel);
        set => AnthropicApiModel = value?.Id ?? AnthropicModelCatalog.DefaultModelId;
    }

    // Blank means "use DefaultPromptTemplate" - PromptViewModel applies that same fallback
    // when it reads this setting fresh on every run.
    [ObservableProperty]
    private string promptTemplateOverride = string.Empty;

    // The built-in instructions (Paarde.Core/Prompts/TutorialInstructionPromptV1.txt),
    // shown read-only so the user has something to compare their override against or copy from -
    // never itself persisted, so a future template version bump is picked up automatically.
    public string DefaultPromptTemplate
    {
        get;
    }

    public ICommand SwitchThemeCommand
    {
        get;
    }

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService, ISecureStorageService secureStorageService, IPromptTemplateProvider promptTemplateProvider, LanguageServerOptionsProvider languageServerOptionsProvider, RunConfigurationsProvider runConfigurationsProvider, IHeadroomCoordinator headroomCoordinator, IHeadroomStatusStore headroomStatusStore, IWindowAppearanceService windowAppearanceService, ILogger<SettingsViewModel>? logger = null)
    {
        _themeSelectorService = themeSelectorService;
        _windowAppearanceService = windowAppearanceService;
        _localSettingsService = localSettingsService;
        _secureStorageService = secureStorageService;
        _languageServerOptionsProvider = languageServerOptionsProvider;
        _runConfigurationsProvider = runConfigurationsProvider;
        _headroomCoordinator = headroomCoordinator;
        _headroomStatusStore = headroomStatusStore;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _theme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();
        DefaultPromptTemplate = promptTemplateProvider.GetTutorialInstructionPrompt();

        SwitchThemeCommand = new RelayCommand<AppTheme>(
            async (param) =>
            {
                if (Theme != param)
                {
                    Theme = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });
    }

    // Async void because INavigationAware.OnNavigatedTo returns void - wrapped so a
    // corrupt LocalSettings.json (ReadSettingAsync's JSON deserialization is the one thing here
    // that can actually throw) is logged and leaves the already-initialized defaults in place,
    // rather than unwinding unobserved out of NavigationService.OnNavigated.
    public async void OnNavigatedTo(object parameter)
    {
        try
        {
            var savedExecutablePath = await _localSettingsService.ReadSettingAsync<string>(ClaudeCliSettingsKeys.ExecutablePath);
            ClaudeCliExecutablePath = string.IsNullOrWhiteSpace(savedExecutablePath) ? ClaudeCliOptions.Default.ExecutablePath : savedExecutablePath;

            var savedArguments = await _localSettingsService.ReadSettingAsync<string>(ClaudeCliSettingsKeys.Arguments);
            ClaudeCliArguments = string.IsNullOrWhiteSpace(savedArguments) ? ClaudeCliOptions.Default.Arguments : savedArguments;

            AgentToolsEnabled = await _localSettingsService.ReadSettingAsync<bool?>(ClaudeCliSettingsKeys.AgentToolsEnabled) ?? false;

            // Unlike ClaudeCliArguments above, a saved-but-blank value here is a deliberate "skip" -
            // not "nothing saved yet" - so it's read as-is rather than falling back to the "dotnet
            // build"/"dotnet test" field defaults.
            BuildCommand = await _localSettingsService.ReadSettingAsync<string>(BuildTestSettingsKeys.BuildCommand) ?? BuildCommand;
            TestCommand = await _localSettingsService.ReadSettingAsync<string>(BuildTestSettingsKeys.TestCommand) ?? TestCommand;

            var languageServerOptions = await _languageServerOptionsProvider.GetOptionsAsync();

            LanguageServerRows.Clear();
            foreach (var options in languageServerOptions)
            {
                LanguageServerRows.Add(new LanguageServerOptionsRowViewModel(options, OnLanguageServerRowChanged));
            }

            _runConfigurationsWorkingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory) ?? string.Empty;
            HasRunConfigurationsWorkingDirectory = !string.IsNullOrWhiteSpace(_runConfigurationsWorkingDirectoryPath);

            RunConfigurationRows.Clear();
            if (HasRunConfigurationsWorkingDirectory)
            {
                var runConfigurationsSnapshot = await _runConfigurationsProvider.GetConfigurationsAsync(_runConfigurationsWorkingDirectoryPath);
                foreach (var configuration in runConfigurationsSnapshot.Configurations)
                {
                    RunConfigurationRows.Add(new RunConfigurationRowViewModel(configuration, OnRunConfigurationRowChanged, OnRunConfigurationRowDeleteRequested));
                }
            }

            PromptTemplateOverride = await _localSettingsService.ReadSettingAsync<string>(PromptTemplateSettingsKeys.Override) ?? string.Empty;

            var savedRunnerKind = await _localSettingsService.ReadSettingAsync<string>(LlmRunnerSettingsKeys.SelectedRunner);
            SelectedRunnerKind = Enum.TryParse<LlmRunnerKind>(savedRunnerKind, out var runnerKind) ? runnerKind : LlmRunnerKind.ClaudeCli;

            AnthropicApiKey = await _secureStorageService.ReadAsync(AnthropicApiSettingsKeys.ApiKey) ?? string.Empty;

            var savedModel = await _localSettingsService.ReadSettingAsync<string>(AnthropicApiSettingsKeys.Model);
            AnthropicApiModel = string.IsNullOrWhiteSpace(savedModel) ? AnthropicModelCatalog.DefaultModelId : savedModel;

            _loadedInterfaceMode = await _localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
            InterfaceMode = _loadedInterfaceMode;
            InterfaceModeChangeRequiresRestart = false;

            IsTransparencyEnabled = await _localSettingsService.ReadSettingAsync<bool?>(AppearanceSettingsKeys.TransparencyEnabled) ?? false;

            IsHeadroomIntegrationEnabled = await _localSettingsService.ReadSettingAsync<bool?>(HeadroomSettingsKeys.IntegrationEnabled) ?? false;
            HeadroomStatusMessage = _headroomStatusStore.Message;
            HeadroomStatusSeverity = _headroomStatusStore.Severity;

            // The automatic startup check (App.axaml.cs) can still be running at this point - it
            // awaits CheckAndOfferFixesAsync (which may itself be waiting on a confirm dialog)
            // after already navigating to the first page, so a result read just above can be
            // stale. ResultChanged fires when that check (or a later manual one) finishes;
            // OnNavigatedFrom unsubscribes since this view model is transient and a new instance
            // is created on the next visit.
            _headroomStatusStore.ResultChanged += OnHeadroomStatusChanged;
        }
        // AnthropicApiKey's ReadAsync (SecureStorageService) additionally throws FormatException on a
        // corrupted base64 blob or System.Security.Cryptography.CryptographicException on a corrupted/
        // tampered ciphertext (AES-GCM's tag check) or wrong-user DPAPI data - every other read in this
        // method is a plain settings read, same surface as everywhere else that narrows this catch.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Newtonsoft.Json.JsonException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            _logger.LogError(ex, "Failed to load saved settings while navigating to the Settings page.");
        }
    }

    public void OnNavigatedFrom()
    {
        _headroomStatusStore.ResultChanged -= OnHeadroomStatusChanged;
    }

    private void OnHeadroomStatusChanged()
    {
        HeadroomStatusMessage = _headroomStatusStore.Message;
        HeadroomStatusSeverity = _headroomStatusStore.Severity;
    }

    [RelayCommand(CanExecute = nameof(CanResetPromptTemplate))]
    private void ResetPromptTemplate() => PromptTemplateOverride = string.Empty;

    private bool CanResetPromptTemplate() => !string.IsNullOrEmpty(PromptTemplateOverride);

    partial void OnClaudeCliExecutablePathChanged(string value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.ExecutablePath, value);

    partial void OnClaudeCliArgumentsChanged(string value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.Arguments, value);

    partial void OnAgentToolsEnabledChanged(bool value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.AgentToolsEnabled, value);

    partial void OnIsTransparencyEnabledChanged(bool value)
    {
        _localSettingsService.SaveSettingAsync(AppearanceSettingsKeys.TransparencyEnabled, value);
        _windowAppearanceService.ApplyTransparency(value);
    }

    partial void OnIsHeadroomIntegrationEnabledChanged(bool value) => _localSettingsService.SaveSettingAsync(HeadroomSettingsKeys.IntegrationEnabled, value);

    [RelayCommand]
    private async Task CheckHeadroomNowAsync()
    {
        var workingDirectoryPath = await _localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory);
        if (string.IsNullOrWhiteSpace(workingDirectoryPath))
        {
            HeadroomStatusMessage = "Open a project before checking Headroom status.";
            HeadroomStatusSeverity = NotificationSeverity.Warning;
            return;
        }

        var (message, severity) = await _headroomCoordinator.CheckAndOfferFixesAsync(workingDirectoryPath, isAutomaticCheck: false);
        HeadroomStatusMessage = message;
        HeadroomStatusSeverity = severity;

        // Also written back to the store so a manual check's result survives navigating away and
        // back too, not just the automatic startup check's (see IHeadroomStatusStore).
        _headroomStatusStore.SetResult(message, severity);
    }

    partial void OnBuildCommandChanged(string value) => _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.BuildCommand, value);

    partial void OnTestCommandChanged(string value) => _localSettingsService.SaveSettingAsync(BuildTestSettingsKeys.TestCommand, value);

    private void OnLanguageServerRowChanged(LanguageServerOptionsRowViewModel row) =>
        _languageServerOptionsProvider.SaveOptionsAsync(LanguageServerRows.Select(r => r.ToOptions()).ToList());

    partial void OnHasRunConfigurationsWorkingDirectoryChanged(bool value) => AddRunConfigurationCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(HasRunConfigurationsWorkingDirectory))]
    private void AddRunConfiguration()
    {
        var configuration = new RunConfiguration(
            Id: Guid.NewGuid().ToString(),
            Name: "New configuration",
            LanguageId: "csharp",
            WorkingDirectoryPath: _runConfigurationsWorkingDirectoryPath,
            ProjectRelativePath: string.Empty,
            BuildCommand: string.Empty,
            RunCommand: string.Empty,
            IsAutoDetected: false);

        RunConfigurationRows.Add(new RunConfigurationRowViewModel(configuration, OnRunConfigurationRowChanged, OnRunConfigurationRowDeleteRequested));
        SaveRunConfigurationRows();
    }

    private void OnRunConfigurationRowChanged(RunConfigurationRowViewModel row) => SaveRunConfigurationRows();

    private void OnRunConfigurationRowDeleteRequested(RunConfigurationRowViewModel row)
    {
        RunConfigurationRows.Remove(row);
        SaveRunConfigurationRows();
    }

    private void SaveRunConfigurationRows()
    {
        if (!HasRunConfigurationsWorkingDirectory)
        {
            return;
        }

        _runConfigurationsProvider.SaveConfigurationsAsync(_runConfigurationsWorkingDirectoryPath, RunConfigurationRows.Select(r => r.ToConfiguration()).ToList());
    }

    partial void OnSelectedRunnerKindChanged(LlmRunnerKind value) => _localSettingsService.SaveSettingAsync(LlmRunnerSettingsKeys.SelectedRunner, value.ToString());

    partial void OnInterfaceModeChanged(InterfaceMode value)
    {
        _localSettingsService.SaveSettingAsync(InterfaceModeSettingsKeys.Mode, value);
        InterfaceModeChangeRequiresRestart = value != _loadedInterfaceMode;
    }

    partial void OnAnthropicApiKeyChanged(string value) => _secureStorageService.SaveAsync(AnthropicApiSettingsKeys.ApiKey, value);

    partial void OnAnthropicApiModelChanged(string value)
    {
        _localSettingsService.SaveSettingAsync(AnthropicApiSettingsKeys.Model, value);
        OnPropertyChanged(nameof(SelectedAnthropicModel));
    }

    partial void OnPromptTemplateOverrideChanged(string value)
    {
        _localSettingsService.SaveSettingAsync(PromptTemplateSettingsKeys.Override, value);
        ResetPromptTemplateCommand.NotifyCanExecuteChanged();
    }

    private static string GetVersionDescription()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version!;

        return $"{"AppDisplayName".Resolve()} - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }
}
