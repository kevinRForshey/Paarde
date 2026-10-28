using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Avalonia.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.ViewModels;
using Paarde.Avalonia.Views;
using Paarde.Core.Contracts.Services;
using Paarde.Core.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Serilog;
using Serilog.Events;

namespace Paarde.Avalonia;

public partial class App : Application
{
    // The .NET Generic Host provides dependency injection, configuration, logging, and other services.
    // https://docs.microsoft.com/dotnet/core/extensions/generic-host
    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.axaml.cs.");
        }

        return service;
    }

    public static T GetKeyedService<T>(object key)
        where T : class
    {
        if ((Current as App)!.Host.Services.GetKeyedService(typeof(T), key) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered with key '{key}' in ConfigureServices within App.axaml.cs.");
        }

        return service;
    }

    public static Window? MainWindow
    {
        get; private set;
    }

    public App()
    {
        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        // T9.2.1: replaces CreateDefaultBuilder's default ILoggerFactory wiring with Serilog, writing
        // a daily-rolling log file under <ApplicationDataFolder>/Logs - this is the one a user without
        // a terminal/debugger attached can actually find and include in a bug report. Serilog's File
        // sink also handles rotation/retention (14 days) for free, which a hand-rolled provider would
        // have had to reimplement. Every ILogger<T> injected elsewhere is unaffected: Serilog.Extensions
        // .Hosting bridges straight onto the same Microsoft.Extensions.Logging.ILogger<T> abstraction.
        // Configured here, ahead of ConfigureServices below, since logging has to exist before any
        // registered service can receive an ILogger<T>.
        UseSerilog((context, loggerConfiguration) =>
        {
            var applicationDataFolder = ResolveApplicationDataFolder(context.Configuration);
            var logFilePath = Path.Combine(applicationDataFolder, "Logs", "log-.txt");

            loggerConfiguration.
            MinimumLevel.Information().
            MinimumLevel.Override("Microsoft", LogEventLevel.Warning).
            WriteTo.Console().
            WriteTo.Debug().
            WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
        }).
        ConfigureServices((context, services) =>
        {
            // Services
            services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
            services.AddSingleton<ISecureStorageService, SecureStorageService>();
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<ClaudeCliOptionsProvider>();
            services.AddSingleton<AnthropicApiOptionsProvider>();

            // T6.3.2/T6.3.3: ILlmRunner itself resolves to LlmRunnerSelector, which picks between
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
            services.AddTransient<IFolderPickerService, FolderPickerService>();
            services.AddTransient<IFilePickerService, FilePickerService>();
            services.AddTransient<IFileSavePickerService, FileSavePickerService>();
            services.AddSingleton<IPromptResultStore, PromptResultStore>();
            services.AddSingleton<IPromptTemplateProvider, PromptTemplateProvider>();
            services.AddTransient<IFileChangeApplyService, FileChangeApplyService>();
            services.AddSingleton<IApplyBackupStore, ApplyBackupStore>();
            services.AddSingleton<IApplyHistoryStore, ApplyHistoryStore>();
            services.AddSingleton<IApplyHistoryLog, ApplyHistoryLog>();

            // T7.1.1: persists each run to %AppData%/<ApplicationDataFolder>/Sessions/*.json - reads
            // the same LocalSettingsOptions.ApplicationDataFolder LocalSettingsService uses (rather
            // than hardcoding a second copy of that folder name) so a user-configured data folder
            // covers sessions too.
            services.AddSingleton<IRunSessionStore>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<LocalSettingsOptions>>().Value;
                var applicationDataFolder = ResolveApplicationDataFolder(options);
                var sessionsFolder = Path.Combine(applicationDataFolder, "Sessions");

                return new RunSessionStore(
                    sp.GetRequiredService<IFileService>(),
                    sp.GetRequiredService<IPromptResultStore>(),
                    sp.GetRequiredService<IApplyHistoryLog>(),
                    sessionsFolder);
            });

            services.AddTransient<IGitStatusService, GitStatusService>();
            services.AddTransient<IBuildTestRunner, ProcessCommandRunner>();

            // T14.2.1/T14.2.2: singleton so its per-working-directory TF-IDF index survives across
            // prompts in the same session instead of re-tokenizing the whole project on every
            // "Suggest relevant files" click.
            services.AddSingleton<ICodebaseIndexService, TfIdfCodebaseIndexService>();

            // Epic 17 (T17.2.1)/Epic 18 (specs/multi-language-support): singleton, not transient
            // like the per-call runners above - ILanguageServerSessionManager owns zero-or-more
            // long-lived per-language server processes tied to Explorer's current working
            // directory, started/stopped by ExplorerViewModel rather than spun up fresh per call.
            services.AddSingleton<LanguageServerOptionsProvider>();
            services.AddSingleton<IProjectTypeDetector, ProjectTypeDetector>();
            services.AddSingleton<ILanguageServerSessionManager>(sp => new LanguageServerSessionManager(
                sp.GetRequiredService<IProjectTypeDetector>(),
                sp.GetRequiredService<LanguageServerOptionsProvider>().GetOptionsAsync,
                logger: sp.GetRequiredService<ILogger<LanguageServerSessionManager>>()));

            // specs/ide-workbench-shell, R4: subscribes to ILanguageServerSessionManager directly
            // (not relayed through ExplorerViewModel), so it has to be a singleton constructed
            // before any page navigates anywhere - WorkbenchDockViewModel's own eager construction
            // (via ShellViewModel) is what actually guarantees that, the same way RunPanelViewModel
            // already guarantees it for Build/Run.
            services.AddSingleton<IDiagnosticsAggregator, DiagnosticsAggregator>();

            // Epic 20 (specs/build-and-run): RunConfigurationsProvider/RunPanelViewModel are
            // singletons for the same reason ILanguageServerSessionManager above is - they back
            // ShellView's persistent toolbar (R5.1), not any one page.
            services.AddSingleton<IRunConfigurationDetector, RunConfigurationDetector>();
            services.AddSingleton<RunConfigurationsProvider>();

            // Epic 21 (specs/terminal-emulation T6): RunPanelViewModel starts processes through
            // this instead of the retired IRunProcessManager - singleton because it owns at most
            // one long-lived PTY-backed process at a time, the same ownership shape
            // ILanguageServerSessionManager has for its per-language server processes.
            services.AddSingleton<IPtyProcessManager, PtyProcessManager>();

            // specs/terminal-emulation T7: TerminalViewModel gets its own, separately keyed
            // PtyProcessManager instance rather than sharing RunPanelViewModel's - Run's
            // persistent toolbar and the Terminal page are independent features (same as the
            // pre-epic IRunProcessManager/ITerminalCommandRunner split), so a command typed into
            // one must not stop or report status for whatever's running in the other.
            services.AddKeyedSingleton<IPtyProcessManager, PtyProcessManager>(TerminalViewModel.PtyProcessManagerKey);
            services.AddSingleton<IWorkingDirectoryContext, WorkingDirectoryContext>();
            services.AddSingleton<IActiveEditorContext, ActiveEditorContext>();
            services.AddSingleton<RunPanelViewModel>();
            services.AddSingleton<StatusBarViewModel>();

            // specs/ide-workbench-shell R1.3: promoted from AddTransient so the Terminal page and
            // the workbench dock's Terminal tab share the exact same instance/session - see this
            // class's own remark.
            services.AddSingleton<TerminalViewModel>();
            services.AddSingleton<WorkbenchDockViewModel>();
            services.AddTransient<IApplyOrchestrationService, ApplyOrchestrationService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();
            services.AddTransient<IDialogService, DialogService>();

            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<INavigationService, NavigationService>();

            // Core Services
            services.AddSingleton<IFileService, FileService>();

            // Views and ViewModels
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<SettingsView>();
            services.AddTransient<TutorialViewModel>();
            services.AddTransient<TutorialView>();
            services.AddTransient<HistoryViewModel>();
            services.AddTransient<HistoryView>();
            services.AddTransient<ExplorerViewModel>();
            services.AddTransient<ExplorerView>();
            services.AddTransient<TerminalView>();
            services.AddTransient<DiffsViewModel>();
            services.AddTransient<DiffsView>();
            services.AddTransient<PromptViewModel>();
            services.AddTransient<PromptView>();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainView>();
            services.AddTransient<ShellView>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();
    }

    // T9.2.1: shared by the logging setup above (which runs ahead of DI and so only has
    // IConfiguration to read LocalSettingsOptions from) and the IRunSessionStore registration
    // (which already has it bound via IOptions<LocalSettingsOptions>) - previously each resolved
    // this path with its own duplicated default-folder literal; now there's exactly one.
    private static string ResolveApplicationDataFolder(IConfiguration configuration) =>
        ResolveApplicationDataFolder(configuration.GetSection(nameof(LocalSettingsOptions)).Get<LocalSettingsOptions>() ?? new LocalSettingsOptions());

    private static string ResolveApplicationDataFolder(LocalSettingsOptions options)
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(localApplicationData, options.ApplicationDataFolder ?? "Paarde.Avalonia/ApplicationData");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // T9.2.2: a last-resort safety net for whatever an inner try/catch didn't - these are the two
    // places .NET surfaces an exception that unwound past every frame on its own thread/task
    // rather than one this app explicitly awaited. Neither handler can stop the process from
    // terminating (AppDomain.UnhandledException always precedes a crash; an unobserved task
    // exception merely stops it from being rethrown on the finalizer thread), but logging here
    // means a crash still leaves a record in the same file a user would be asked to send.
    private void RegisterGlobalCrashHandlers()
    {
        var logger = GetService<ILogger<App>>();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception on {Thread}; the process is terminating.", Environment.CurrentManagedThreadId);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalCrashHandlers();
        GetService<ILogger<App>>().LogInformation("Application starting.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow must be assigned synchronously before this method returns: Avalonia's
            // desktop lifetime shows/tracks whatever window is assigned at that point, so window creation
            // can't sit behind an await. Theme init/navigation don't need to block that, so they run after.
            var shell = GetService<ShellView>();
            var window = new Views.MainWindow { Content = shell };
            MainWindow = window;
            desktop.MainWindow = window;

#if DEBUG
            // Avalonia.Diagnostics is Debug-only (Paarde.Avalonia.csproj), same as this call.
            // The Avalonia.Generators default (F12) is disabled in that csproj because it collides
            // with CodeEditor's own F12 go-to-definition binding (Epic 17) - Ctrl+Shift+F12 instead.
            window.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Control | KeyModifiers.Shift));
#endif

            // On Linux, Avalonia's X11 backend keeps a D-Bus connection alive for the app's lifetime to
            // watch for OS theme changes. Its teardown races Dispatcher.UIThread.InvokeShutdown() (which
            // ClassicDesktopStyleApplicationLifetime calls right after raising Exit), surfacing as an
            // unhandled TaskCanceledException from a background thread. Exiting here, before that
            // InvokeShutdown() call, sidesteps the race instead of letting Avalonia's internal teardown hit it.
            desktop.Exit += (_, _) =>
            {
                // Epic 17 (T17.2.1)/Epic 18: every language server is a child process with no
                // owner other than this app - Environment.Exit(0) below terminates this process
                // immediately and would otherwise orphan it/them. Each client's StopAsync kill is
                // bounded (a couple of seconds at most), so blocking briefly here is acceptable on
                // the way out.
                GetService<ILanguageServerSessionManager>().StopAllAsync().GetAwaiter().GetResult();

                // Epic 20 (specs/build-and-run R4.6)/Epic 21 (specs/terminal-emulation R1.2, T6,
                // T7): a Run or Terminal session is the same kind of app-owned, otherwise-orphaned
                // child process - Stop() is synchronous and already bounded by killing the process
                // tree, so no extra waiting is needed here. Two separately keyed instances (T7),
                // so both need stopping independently.
                GetService<IPtyProcessManager>().Stop();
                GetKeyedService<IPtyProcessManager>(TerminalViewModel.PtyProcessManagerKey).Stop();

                Environment.Exit(0);
            };

            InitializeThemeAndNavigate();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // T9.2.3: async void because OnFrameworkInitializationCompleted (an override, so its own
    // signature can't change) can't await it - any exception thrown here would otherwise unwind
    // past that override and surface as an AppDomain.UnhandledException crash (see
    // RegisterGlobalCrashHandlers above) instead of a reported, recoverable failure. Wrapping it
    // here settles that: the app still comes up (with whatever theme Avalonia's default already
    // applied and no restored session) rather than never showing a window at all.
    private async void InitializeThemeAndNavigate()
    {
        try
        {
            var themeSelectorService = GetService<IThemeSelectorService>();
            await themeSelectorService.InitializeAsync();
            await themeSelectorService.SetRequestedThemeAsync();

            // T7.1.2: pushes the last recorded run's tutorial/files/apply-log into IPromptResultStore/
            // IApplyHistoryLog before anything navigates, so the Tutorial and Diffs pages already have
            // it whenever the user first visits them - a no-op if no run has ever been recorded.
            GetService<IRunSessionStore>().RestoreLastSession();

            GetService<INavigationService>().NavigateTo(typeof(MainViewModel).FullName!);
        }
        catch (Exception ex)
        {
            GetService<ILogger<App>>().LogError(ex, "Failed to initialize theme/restore the last session/navigate to the main page on startup.");
        }
    }
}
