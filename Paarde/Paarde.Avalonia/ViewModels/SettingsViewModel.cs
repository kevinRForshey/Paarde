using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;

using Avalonia.Styling;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Contracts.ViewModels;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.Strings;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Paarde.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableRecipient, INavigationAware
{
    private readonly IThemeSelectorService _themeSelectorService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly ISecureStorageService _secureStorageService;
    private readonly LanguageServerOptionsProvider _languageServerOptionsProvider;
    private readonly RunConfigurationsProvider _runConfigurationsProvider;
    private readonly ILogger<SettingsViewModel> _logger;

    // Epic 20 (specs/build-and-run R2.6): the working directory Run configurations below belong
    // to - read once in OnNavigatedTo the same way TerminalViewModel reads it, not re-exposed as
    // its own editable field here since this page doesn't change the working directory, only
    // edits what's configured for whichever one is currently selected on the Prompt page.
    private string _runConfigurationsWorkingDirectoryPath = string.Empty;

    [ObservableProperty]
    private ThemeVariant _themeVariant;

    [ObservableProperty]
    private string _versionDescription;

    [ObservableProperty]
    private string claudeCliExecutablePath = ClaudeCliOptions.Default.ExecutablePath;

    [ObservableProperty]
    private string claudeCliArguments = ClaudeCliOptions.Default.Arguments;

    // Epic 11 (F11.1.1): off by default - this changes what the model can *do* mid-run (read/grep/
    // glob the project), not just how it's invoked, so it shouldn't turn on silently. Shared by
    // whichever ILlmRunner is selected (T11.1.2/T11.1.3).
    [ObservableProperty]
    private bool agentToolsEnabled;

    // Epic 12 (F12.1.2): the commands run after an apply, if non-blank, to check the result still
    // builds/passes its tests. Blank means "skip" rather than falling back to some default command
    // name - not every working directory is a .NET project, and a wrong guess shelling out to a
    // nonexistent command would be a worse default than doing nothing.
    [ObservableProperty]
    private string buildCommand = "dotnet build";

    [ObservableProperty]
    private string testCommand = "dotnet test";

    // Epic 18 (R6.1): one row per configured language, replacing Epic 17's single flat
    // LspExecutablePath/LspArguments pair - populated from LanguageServerOptionsProvider in
    // OnNavigatedTo, saved back as a whole collection on any row's edit.
    public ObservableCollection<LanguageServerOptionsRowViewModel> LanguageServerRows { get; } = [];

    // Epic 20 (specs/build-and-run R2.6): one row per Run configuration for the current working
    // directory - populated from RunConfigurationsProvider in OnNavigatedTo, saved back as a
    // whole collection on any row's edit/delete, same shape as LanguageServerRows above.
    public ObservableCollection<RunConfigurationRowViewModel> RunConfigurationRows { get; } = [];

    [ObservableProperty]
    private bool hasRunConfigurationsWorkingDirectory;

    // T6.3.2/T6.3.3: which ILlmRunner implementation LlmRunnerSelector should delegate to.
    [ObservableProperty]
    private LlmRunnerKind selectedRunnerKind = LlmRunnerKind.ClaudeCli;

    // Persisted through ISecureStorageService (encrypted at rest), not ILocalSettingsService -
    // see that interface's own comment for why.
    [ObservableProperty]
    private string anthropicApiKey = string.Empty;

    [ObservableProperty]
    private string anthropicApiModel = AnthropicModelCatalog.DefaultModelId;

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

    // T5.3.2: blank means "use DefaultPromptTemplate" - PromptViewModel applies that same fallback
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

    public SettingsViewModel(IThemeSelectorService themeSelectorService, ILocalSettingsService localSettingsService, ISecureStorageService secureStorageService, IPromptTemplateProvider promptTemplateProvider, LanguageServerOptionsProvider languageServerOptionsProvider, RunConfigurationsProvider runConfigurationsProvider, ILogger<SettingsViewModel>? logger = null)
    {
        _themeSelectorService = themeSelectorService;
        _localSettingsService = localSettingsService;
        _secureStorageService = secureStorageService;
        _languageServerOptionsProvider = languageServerOptionsProvider;
        _runConfigurationsProvider = runConfigurationsProvider;
        _logger = logger ?? NullLogger<SettingsViewModel>.Instance;
        _themeVariant = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();
        DefaultPromptTemplate = promptTemplateProvider.GetTutorialInstructionPrompt();

        SwitchThemeCommand = new RelayCommand<ThemeVariant>(
            async (param) =>
            {
                if (param != null && ThemeVariant != param)
                {
                    ThemeVariant = param;
                    await _themeSelectorService.SetThemeAsync(param);
                }
            });
    }

    // T9.2.3: async void because INavigationAware.OnNavigatedTo returns void - wrapped so a
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load saved settings while navigating to the Settings page.");
        }
    }

    public void OnNavigatedFrom()
    {
    }

    [RelayCommand(CanExecute = nameof(CanResetPromptTemplate))]
    private void ResetPromptTemplate() => PromptTemplateOverride = string.Empty;

    private bool CanResetPromptTemplate() => !string.IsNullOrEmpty(PromptTemplateOverride);

    partial void OnClaudeCliExecutablePathChanged(string value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.ExecutablePath, value);

    partial void OnClaudeCliArgumentsChanged(string value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.Arguments, value);

    partial void OnAgentToolsEnabledChanged(bool value) => _localSettingsService.SaveSettingAsync(ClaudeCliSettingsKeys.AgentToolsEnabled, value);

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
