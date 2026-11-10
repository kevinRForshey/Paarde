using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

using Paarde.Core.Contracts.Services;
using Paarde.Presentation;
using Paarde.Presentation.Contracts.Services;
using Paarde.Presentation.Models;
using Paarde.Presentation.Services;
using Paarde.Presentation.ViewModels;
using Paarde.WinUI.Contracts.Services;
using Paarde.WinUI.Controls;
using Paarde.WinUI.Services;
using Paarde.WinUI.ViewModels;
using Paarde.WinUI.Views;

using Serilog;
using Serilog.Events;

namespace Paarde.WinUI;

// specs/winui-frontend: the WinUI composition root - same Generic Host, Serilog and startup sequence
// as Paarde.Avalonia's App.axaml.cs, with the shared graph coming from AddPaardePresentation() and only
// the WinUI implementations registered here.
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
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static T GetKeyedService<T>(object key)
        where T : class
    {
        if ((Current as App)!.Host.Services.GetKeyedService(typeof(T), key) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered with key '{key}' in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static MainWindow? MainWindow
    {
        get; private set;
    }

    public App()
    {
        InitializeComponent();

        // Captured here because the constructor runs on the UI thread; see WinUiDispatcher.
        var uiDispatcher = new WinUiDispatcher(DispatcherQueue.GetForCurrentThread());

        Host = Microsoft.Extensions.Hosting.Host.
        CreateDefaultBuilder().
        UseContentRoot(AppContext.BaseDirectory).
        // T9.2.1: same Serilog setup as Paarde.Avalonia, into the same shared Logs folder, but with
        // its own file prefix so both apps can run at once without contending for one rolling file.
        UseSerilog((context, loggerConfiguration) =>
        {
            var applicationDataFolder = ResolveApplicationDataFolder(context.Configuration);
            var logFilePath = Path.Combine(applicationDataFolder, "Logs", "winui-log-.txt");

            loggerConfiguration.
            MinimumLevel.Information().
            MinimumLevel.Override("Microsoft", LogEventLevel.Warning).
            WriteTo.Console().
            WriteTo.Debug().
            WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
        }).
        ConfigureServices((context, services) =>
        {
            // specs/winui-frontend R1: the Core + Presentation graph shared with every front end.
            services.AddPaardePresentation();

            // WinUI implementations of Presentation contracts.
            services.AddSingleton<IUiDispatcher>(uiDispatcher);
            services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
            services.AddSingleton<IWindowAppearanceService, WindowAppearanceService>();
            services.AddTransient<IFolderPickerService, FolderPickerService>();
            services.AddTransient<IFilePickerService, FilePickerService>();
            services.AddTransient<IFileSavePickerService, FileSavePickerService>();
            services.AddTransient<IDialogService, DialogService>();

            services.AddSingleton<IPageService, PageService>();
            services.AddSingleton<NavigationService>();
            services.AddSingleton<IFrameNavigationService>(sp => sp.GetRequiredService<NavigationService>());
            services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<NavigationService>());
            services.AddTransient<INavigationViewService, NavigationViewService>();

            services.AddTransient<ShellView>();
            services.AddTransient<ShellViewModel>();

            // Configuration
            services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
        }).
        Build();

        UnhandledException += (_, e) =>
            GetService<ILogger<App>>().LogCritical(e.Exception, "Unhandled exception on the UI thread.");
    }

    // T9.2.1: the logging setup above runs ahead of DI and so only has IConfiguration to read
    // LocalSettingsOptions from; ApplicationDataPaths is the one shared resolution.
    private static string ResolveApplicationDataFolder(IConfiguration configuration) =>
        ApplicationDataPaths.Resolve(configuration.GetSection(nameof(LocalSettingsOptions)).Get<LocalSettingsOptions>()?.ApplicationDataFolder);

    // T9.2.2: a last-resort record for exceptions that unwound past every frame on their own
    // thread/task - same handlers as Paarde.Avalonia.
    private static void RegisterGlobalCrashHandlers()
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        RegisterGlobalCrashHandlers();
        GetService<ILogger<App>>().LogInformation("Application starting.");

        // The editor's WebView2 cache lives with everything else this app writes, rather than next
        // to the executable (which may not be writable once installed).
        CodeEditor.UserDataFolder = Path.Combine(
            ApplicationDataPaths.Resolve(GetService<IOptions<LocalSettingsOptions>>().Value.ApplicationDataFolder),
            "WebView2");

        var window = new MainWindow();
        window.SetShell(GetService<ShellView>());
        MainWindow = window;

        // Every language server, Run and Terminal session is a child process with no owner other
        // than this app, so stop them on the way out rather than orphan them (same as Avalonia's
        // desktop.Exit handler). Each stop is bounded, so blocking briefly here is acceptable.
        window.Closed += (_, _) =>
        {
            GetService<ILanguageServerSessionManager>().StopAllAsync().GetAwaiter().GetResult();
            GetService<IPtyProcessManager>().Stop();
            GetKeyedService<IPtyProcessManager>(TerminalViewModel.PtyProcessManagerKey).Stop();
        };

        window.Activate();

        InitializeThemeAndNavigate();
    }

    // T9.2.3: async void because OnLaunched can't await it - wrapped so a failure here is logged
    // and the app still comes up, rather than crashing before showing anything.
    private async void InitializeThemeAndNavigate()
    {
        try
        {
            var themeSelectorService = GetService<IThemeSelectorService>();
            await themeSelectorService.InitializeAsync();
            await themeSelectorService.SetRequestedThemeAsync();

            // T7.1.2: restore the last recorded run before anything navigates.
            GetService<IRunSessionStore>().RestoreLastSession();

            // Epic 26 (specs/interface-mode R2): which shell the app starts into.
            var localSettingsService = GetService<ILocalSettingsService>();
            var interfaceMode = await localSettingsService.ReadSettingAsync<InterfaceMode?>(InterfaceModeSettingsKeys.Mode) ?? InterfaceMode.EditorFocused;
            var startupViewModelType = interfaceMode == InterfaceMode.FlowWizard ? typeof(MainViewModel) : typeof(ExplorerViewModel);
            GetService<INavigationService>().NavigateTo(startupViewModelType.FullName!);

            // specs/fluent-design-pass R1: off by default.
            var transparencyEnabled = await localSettingsService.ReadSettingAsync<bool?>(AppearanceSettingsKeys.TransparencyEnabled) ?? false;
            GetService<IWindowAppearanceService>().ApplyTransparency(transparencyEnabled);

            // Off by default; skipped if no project has ever been opened.
            var headroomIntegrationEnabled = await localSettingsService.ReadSettingAsync<bool?>(HeadroomSettingsKeys.IntegrationEnabled) ?? false;
            var savedWorkingDirectory = await localSettingsService.ReadSettingAsync<string>(WorkingDirectorySettingsKeys.WorkingDirectory);

            if (headroomIntegrationEnabled && !string.IsNullOrWhiteSpace(savedWorkingDirectory))
            {
                var (message, severity) = await GetService<IHeadroomCoordinator>().CheckAndOfferFixesAsync(savedWorkingDirectory, isAutomaticCheck: true);
                GetService<IHeadroomStatusStore>().SetResult(message, severity);
            }
        }
        catch (Exception ex)
        {
            GetService<ILogger<App>>().LogError(ex, "Failed to initialize theme/restore the last session/navigate to the start page on startup.");
        }
    }
}
