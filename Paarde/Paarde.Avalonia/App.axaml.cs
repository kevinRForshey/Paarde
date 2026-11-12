using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using Paarde.Avalonia.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Avalonia.Services;
using Paarde.Avalonia.ViewModels;
using Paarde.Avalonia.Views;
using Paarde.Core.Contracts.Services;
using Paarde.Presentation;
using Paarde.Presentation.Contracts.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Events;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;

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
        // Replaces CreateDefaultBuilder's default ILoggerFactory wiring with Serilog, writing
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
            // The Core + Presentation graph shared with every front end.
            services.AddPaardePresentation();

            // Avalonia-specific implementations of Presentation contracts.
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddTransient<IFolderPickerService, FolderPickerService>();
            services.AddTransient<IFilePickerService, FilePickerService>();
            services.AddTransient<IFileSavePickerService, FileSavePickerService>();
            services.AddTransient<INavigationViewService, NavigationViewService>();
            services.AddTransient<IDialogService, DialogService>();

            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<NavigationService>();
            services.AddSingleton<IFrameNavigationService>(sp => sp.GetRequiredService<NavigationService>());
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());

            // View models marshal onto the UI thread through
            // this rather than Avalonia's Dispatcher directly.
            services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
            services.AddSingleton<IWindowAppearanceService, WindowAppearanceService>();

            // Views
            services.AddTransient<SettingsView>();
            services.AddTransient<HistoryView>();
            services.AddTransient<ExplorerView>();
            services.AddTransient<TerminalView>();
            services.AddTransient<PromptView>();
            services.AddTransient<MainView>();
            services.AddTransient<ReviewView>();

            services.AddTransient<ShellView>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();
    }

    // The logging setup above runs ahead of DI and so only has IConfiguration to read
    // LocalSettingsOptions from; ApplicationDataPaths is the one shared resolution.
    private static string ResolveApplicationDataFolder(IConfiguration configuration)
    {
        var configuredFolder = configuration.GetSection(nameof(LocalSettingsOptions)).Get<LocalSettingsOptions>()?.ApplicationDataFolder;

        // Logging is the first thing to open a file in the folder, so the one-time move happens here.
        ApplicationDataPaths.MoveLegacyFolder(configuredFolder);

        return ApplicationDataPaths.Resolve(configuredFolder);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // A last-resort safety net for whatever an inner try/catch didn't - these are the two
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
            // The Avalonia.Generators default is disabled in that csproj because it collides
            // with CodeEditor's own F12 go-to-definition binding - Ctrl+Shift+F12 instead.
            window.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Control | KeyModifiers.Shift));
#endif

            // On Linux, Avalonia's X11 backend keeps a D-Bus connection alive for the app's lifetime to
            // watch for OS theme changes. Its teardown races Dispatcher.UIThread.InvokeShutdown() (which
            // ClassicDesktopStyleApplicationLifetime calls right after raising Exit), surfacing as an
            // unhandled TaskCanceledException from a background thread. Exiting here, before that
            // InvokeShutdown() call, sidesteps the race instead of letting Avalonia's internal teardown hit it.
            desktop.Exit += (_, _) =>
            {
                // Epic 17/Epic 18: every language server is a child process with no
                // owner other than this app - Environment.Exit(0) below terminates this process
                // immediately and would otherwise orphan it/them. Each client's StopAsync kill is
                // bounded (a couple of seconds at most), so blocking briefly here is acceptable on
                // the way out.
                GetService<ILanguageServerSessionManager>().StopAllAsync().GetAwaiter().GetResult();

                // Epic 20/Epic 21 (specs/terminal-emulation R1.2, T6,
                // T7): a Run or Terminal session is the same kind of app-owned, otherwise-orphaned
                // child process - Stop() is synchronous and already bounded by killing the process
                // tree, so no extra waiting is needed here. Two separately keyed instances,
                // so both need stopping independently.
                GetService<IPtyProcessManager>().Stop();
                GetKeyedService<IPtyProcessManager>(TerminalViewModel.PtyProcessManagerKey).Stop();

                Environment.Exit(0);
            };

            InitializeThemeAndNavigate();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Async void because OnFrameworkInitializationCompleted (an override, so its own
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

            // Pushes the last recorded run's tutorial/files/apply-log into IPromptResultStore/
            // IApplyHistoryLog before anything navigates, so the Tutorial and Diffs pages already have
            // it whenever the user first visits them - a no-op if no run has ever been recorded.
            GetService<IRunSessionStore>().RestoreLastSession();

            // AssistantPanelViewModel owns "a run finished -> show Review" (and, in FlowWizard mode,
            // navigating to the Review page). It's a singleton otherwise only created by Explorer/
            // History, so in FlowWizard mode - which starts on Home - a run from Compose went
            // unnoticed. Created after RestoreLastSession so restoring doesn't itself count as a run.
            GetService<AssistantPanelViewModel>();

            // Which shell the app starts into - EditorFocused
            // keeps today's Explorer-first behavior, FlowWizard starts on the Home/Compose/Review
            // flow instead.
            var interfaceMode = await GetService<ILocalSettingsService>().ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
            var startupViewModelType = interfaceMode == InterfaceMode.FlowWizard ? typeof(MainViewModel) : typeof(ExplorerViewModel);
            GetService<INavigationService>().NavigateTo(startupViewModelType.FullName!);

            // Off by default - see AppearanceSettingsKeys/
            // MainWindow.ApplyTransparency.
            var transparencyEnabled = await GetService<ILocalSettingsService>().ReadSettingAsync<bool?>(AppearanceSettingsKeys.TransparencyEnabled) ?? false;
            (MainWindow as Views.MainWindow)?.ApplyTransparency(transparencyEnabled);

            // Off by default, same reasoning as TransparencyEnabled above - see
            // HeadroomSettingsKeys. Skipped entirely if no project has ever been opened (nothing
            // to check yet) - the Prompt page hasn't navigated at this point, so
            // WorkingDirectorySettingsKeys is the only source for "the current project" this early.
            var headroomIntegrationEnabled = await GetService<ILocalSettingsService>().ReadSettingAsync<bool?>(HeadroomSettingsKeys.IntegrationEnabled) ?? false;
            var savedWorkingDirectory = await GetService<ILocalSettingsService>().ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory);

            if (headroomIntegrationEnabled && !string.IsNullOrWhiteSpace(savedWorkingDirectory))
            {
                var (message, severity) = await GetService<IHeadroomCoordinator>().CheckAndOfferFixesAsync(savedWorkingDirectory, isAutomaticCheck: true);
                GetService<IHeadroomStatusStore>().SetResult(message, severity);
            }
        }
        catch (Exception ex)
        {
            GetService<ILogger<App>>().LogError(ex, "Failed to initialize theme/restore the last session/navigate to the Explorer page on startup.");
        }
    }
}
