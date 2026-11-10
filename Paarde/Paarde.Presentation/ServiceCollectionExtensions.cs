using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Services;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

namespace Paarde.Presentation;

// The Core + Presentation service graph every front end shares, registered
// identically by each host. Deliberately excludes anything a host has to supply itself - IUiDispatcher,
// INavigationService, IThemeSelectorService, IWindowAppearanceService, IDialogService, the pickers,
// page/navigation-view services, the shell view model, views - and the
// services.Configure<LocalSettingsOptions>(...) binding, which needs a configuration package
// Presentation doesn't take.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaardePresentation(this IServiceCollection services)
    {
        // Services
        services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
        services.AddSingleton<ISecureStorageService, SecureStorageService>();
        services.AddSingleton<ClaudeCliOptionsProvider>();
        services.AddSingleton<AnthropicApiOptionsProvider>();

        // ILlmRunner itself resolves to LlmRunnerSelector, which picks between
        // these two concrete runners per-call based on the Settings page's runner choice - see
        // LlmRunnerSelector's own comment. Each concrete runner is built directly (not also
        // registered as ILlmRunner) so there's no ambiguity over which one DI would hand to a
        // constructor that asked for "the" ILlmRunner.
        services.AddSingleton(sp => new ClaudeCliRunner(sp.GetRequiredService<ClaudeCliOptionsProvider>().GetOptionsAsync));
        services.AddSingleton(sp => new AnthropicApiRunner(sp.GetRequiredService<AnthropicApiOptionsProvider>().GetOptionsAsync));
        services.AddSingleton<ILlmRunner>(sp => new LlmRunnerSelector(
            sp.GetRequiredService<ILocalSettingsService>(),
            sp.GetRequiredService<ClaudeCliRunner>(),
            sp.GetRequiredService<AnthropicApiRunner>()));
        services.AddSingleton<IPromptResultStore, PromptResultStore>();
        services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
        services.AddTransient<IFileChangeApplyService, FileChangeApplyService>();
        services.AddSingleton<IApplyBackupStore, ApplyBackupStore>();
        services.AddSingleton<IApplyHistoryStore, ApplyHistoryStore>();
        services.AddSingleton<IApplyHistoryLog, ApplyHistoryLog>();

        // Persists each run to %AppData%/<ApplicationDataFolder>/Sessions/*.json - reads
        // the same LocalSettingsOptions.ApplicationDataFolder LocalSettingsService uses (rather
        // than hardcoding a second copy of that folder name) so a user-configured data folder
        // covers sessions too.
        services.AddSingleton<IRunSessionStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LocalSettingsOptions>>().Value;
            var applicationDataFolder = ApplicationDataPaths.Resolve(options.ApplicationDataFolder);
            var sessionsFolder = Path.Combine(applicationDataFolder, "Sessions");

            return new RunSessionStore(
                sp.GetRequiredService<IFileService>(),
                sp.GetRequiredService<IPromptResultStore>(),
                sp.GetRequiredService<IApplyHistoryLog>(),
                sessionsFolder);
        });

        services.AddTransient<IGitStatusService, GitStatusService>();
        services.AddTransient<IBuildTestRunner, ProcessCommandRunner>();
        services.AddTransient<IHeadroomService, HeadroomService>();
        services.AddTransient<IHeadroomCoordinator, HeadroomCoordinator>();
        services.AddSingleton<IHeadroomStatusStore, HeadroomStatusStore>();
        services.AddTransient<IHeadroomPromptSnoozeStore, HeadroomPromptSnoozeStore>();

        // Singleton so its per-working-directory TF-IDF index survives across
        // prompts in the same session instead of re-tokenizing the whole project on every
        // "Suggest relevant files" click.
        services.AddSingleton<ICodebaseIndexService, TfIdfCodebaseIndexService>();

        // Epic 17/Epic 18: singleton, not transient
        // like the per-call runners above - ILanguageServerSessionManager owns zero-or-more
        // long-lived per-language server processes tied to Explorer's current working
        // directory, started/stopped by ExplorerViewModel rather than spun up fresh per call.
        services.AddSingleton<LanguageServerOptionsProvider>();
        services.AddSingleton<IProjectTypeDetector, ProjectTypeDetector>();
        services.AddSingleton<ILanguageServerSessionManager>(sp => new LanguageServerSessionManager(
            sp.GetRequiredService<IProjectTypeDetector>(),
            sp.GetRequiredService<LanguageServerOptionsProvider>().GetOptionsAsync,
            logger: sp.GetRequiredService<ILogger<LanguageServerSessionManager>>()));

        // Subscribes to ILanguageServerSessionManager directly
        // (not relayed through ExplorerViewModel), so it has to be a singleton constructed
        // before any page navigates anywhere - WorkbenchDockViewModel's own eager construction
        // (via the shell view model) is what actually guarantees that, the same way
        // RunPanelViewModel already guarantees it for Build/Run.
        services.AddSingleton<IDiagnosticsAggregator, DiagnosticsAggregator>();

        // RunConfigurationsProvider/RunPanelViewModel are
        // singletons for the same reason ILanguageServerSessionManager above is - they back
        // the shell's persistent toolbar, not any one page.
        services.AddSingleton<IRunConfigurationDetector, RunConfigurationDetector>();
        services.AddSingleton<RunConfigurationsProvider>();

        // RunPanelViewModel starts processes through
        // this instead of the retired IRunProcessManager - singleton because it owns at most
        // one long-lived PTY-backed process at a time, the same ownership shape
        // ILanguageServerSessionManager has for its per-language server processes.
        services.AddSingleton<IPtyProcessManager, PtyProcessManager>();

        // TerminalViewModel gets its own, separately keyed
        // PtyProcessManager instance rather than sharing RunPanelViewModel's - Run's
        // persistent toolbar and the Terminal page are independent features (same as the
        // pre-epic IRunProcessManager/ITerminalCommandRunner split), so a command typed into
        // one must not stop or report status for whatever's running in the other.
        services.AddKeyedSingleton<IPtyProcessManager, PtyProcessManager>(TerminalViewModel.PtyProcessManagerKey);
        services.AddSingleton<IWorkingDirectoryContext, WorkingDirectoryContext>();
        services.AddSingleton<IActiveEditorContext, ActiveEditorContext>();
        services.AddSingleton<RunPanelViewModel>();
        services.AddSingleton<StatusBarViewModel>();

        // Promoted from AddTransient so the Terminal page and
        // the workbench dock's Terminal tab share the exact same instance/session - see this
        // class's own remark.
        services.AddSingleton<TerminalViewModel>();
        services.AddSingleton<WorkbenchDockViewModel>();

        // Quick Open and the command palette - reachable from anywhere via
        // the shell's global key bindings, so each must be one instance, not one per page
        // navigation.
        services.AddSingleton<QuickOpenViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();

        // Go to Symbol in File - same "reachable from anywhere
        // via a global key binding" reason as the two singletons above.
        services.AddSingleton<SymbolQuickPickViewModel>();
        services.AddTransient<IApplyOrchestrationService, ApplyOrchestrationService>();

        // Core Services
        services.AddSingleton<IFileService, FileService>();

        // ViewModels
        services.AddTransient<SettingsViewModel>();
        // AddSingleton rather than AddTransient - the Assistant panel must be
        // reachable from HistoryViewModel's Fork/Rerun without depending on the Explorer page ever
        // having been constructed, and a second accidental instance would mean "Fork" from
        // History silently updates a PromptViewModel/TutorialViewModel nobody is looking at.
        services.AddSingleton<AssistantPanelViewModel>();
        services.AddSingleton<TutorialViewModel>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<ExplorerViewModel>();
        services.AddSingleton<PromptViewModel>();

        // FlowWizard mode's Home page - AddTransient like
        // every other page-scoped view model above, since it isn't reachable from outside
        // navigation the way AssistantPanelViewModel/TutorialViewModel are.
        services.AddTransient<MainViewModel>();

        return services;
    }
}
