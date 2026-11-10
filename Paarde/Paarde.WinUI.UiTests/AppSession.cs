using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace Paarde.WinUI.UiTests;

// One running Paarde.WinUI.exe with its own settings folder, so a test never reads or writes the
// user's real settings. The folder is seeded with a small project as the saved working directory.
public class AppSession : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly Application _application;
    private readonly UIA3Automation _automation = new();

    public AppSession(bool flowWizardMode)
    {
        DataFolder = Directory.CreateTempSubdirectory("PaardeUiTests_").FullName;
        ProjectFolder = Directory.CreateDirectory(Path.Combine(DataFolder, "project")).FullName;

        File.WriteAllText(Path.Combine(ProjectFolder, "Sample.cs"), "namespace Sample;\n\npublic class Greeter\n{\n}\n");
        File.WriteAllText(Path.Combine(ProjectFolder, "notes.md"), "# Notes\n");

        // LocalSettingsService's format: each value is itself a JSON string.
        var settings = new Dictionary<string, string>
        {
            ["Interface.Mode"] = flowWizardMode ? "1" : "0",
            ["SavedWorkingDirectory"] = JsonSerializer.Serialize(ProjectFolder),
        };
        File.WriteAllText(Path.Combine(DataFolder, "LocalSettings.json"), JsonSerializer.Serialize(settings));

        var startInfo = new ProcessStartInfo(FindExecutable())
        {
            UseShellExecute = false,
            WorkingDirectory = ProjectFolder,
        };
        startInfo.Environment["LocalSettingsOptions__ApplicationDataFolder"] = DataFolder;

        _application = Application.Launch(startInfo);
        Window = _application.GetMainWindow(_automation, DefaultTimeout)
            ?? throw new InvalidOperationException("Paarde.WinUI started but its main window never appeared.");

        SetWindowSize(1280, 800);
    }

    public string DataFolder { get; }

    public string ProjectFolder { get; }

    public Window Window { get; }

    public ConditionFactory By => _automation.ConditionFactory;

    public bool HasExited => _application.HasExited;

    // Selects a NavigationView item the way a click does (Invoke raises ItemInvoked, which is what
    // NavigationViewService navigates on) and waits until the shell marks it selected.
    public void NavigateTo(string pageName)
    {
        var item = WaitFor(() => Window.FindFirstDescendant(By.ByControlType(ControlType.ListItem).And(By.ByName(pageName))),
            $"navigation item \"{pageName}\"");

        if (item.Patterns.Invoke.IsSupported)
        {
            item.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            item.Patterns.SelectionItem.Pattern.Select();
        }

        WaitFor(() => item.Patterns.SelectionItem.Pattern.IsSelected.Value ? item : null, $"\"{pageName}\" to become the selected page");

        // Let the page finish its Loaded work (settings reads, tree building) before it's inspected.
        Thread.Sleep(750);
    }

    // Keyboard input goes to whatever window is in front, so never send it unless that's this app.
    public void BringToFront()
    {
        Window.SetForeground();

        var handle = Window.Properties.NativeWindowHandle.Value;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (GetForegroundWindow() != handle)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException("Paarde isn't the foreground window, so no keys were sent. Leave the desktop alone while UI tests run.");
            }

            Thread.Sleep(100);
            Window.SetForeground();
        }
    }

    // Serilog's default file template writes the level as [ERR]/[FTL]. Read shared: the app still
    // has the file open.
    public IReadOnlyList<string> LoggedErrors()
    {
        var logFolder = Path.Combine(DataFolder, "Logs");
        if (!Directory.Exists(logFolder))
        {
            return [];
        }

        var errors = new List<string>();
        foreach (var logFile in Directory.GetFiles(logFolder, "winui-log-*.txt"))
        {
            using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            errors.AddRange(reader.ReadToEnd().Split('\n').Where(line => line.Contains("[ERR]") || line.Contains("[FTL]")).Select(line => line.TrimEnd()));
        }

        return errors;
    }

    public static T WaitFor<T>(Func<T?> find, string description, TimeSpan? timeout = null)
        where T : class =>
        WaitFor(find, () => description, timeout);

    // describe runs only on timeout, so it can report what was there instead.
    public static T WaitFor<T>(Func<T?> find, Func<string> describe, TimeSpan? timeout = null)
        where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            try
            {
                if (find() is { } found)
                {
                    return found;
                }
            }
            catch (COMException)
            {
                // The element went away mid-query (e.g. a page being replaced); try again.
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {describe()}.");
            }

            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        if (!_application.HasExited)
        {
            // A normal close, so the app stops its language servers and terminals itself.
            _application.Close();
            if (!WaitForExit(TimeSpan.FromSeconds(10)))
            {
                _application.Kill();
            }
        }

        _application.Dispose();
        _automation.Dispose();

        // WebView2's helper processes can hold files for a moment after the app exits.
        for (var attempt = 0; attempt < 10 && Directory.Exists(DataFolder); attempt++)
        {
            try
            {
                Directory.Delete(DataFolder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(500);
            }
        }
    }

    private bool WaitForExit(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!_application.HasExited && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
        }

        return _application.HasExited;
    }

    // A fixed size in device-independent pixels, so the layout checks don't depend on the screen.
    private void SetWindowSize(int width, int height)
    {
        var handle = Window.Properties.NativeWindowHandle.Value;
        var scale = GetDpiForWindow(handle) / 96.0;
        MoveWindow(handle, 40, 40, (int)(width * scale), (int)(height * scale), true);
        Thread.Sleep(500);
    }

    // PAARDE_WINUI_EXE overrides; otherwise the newest build next to this test project, matching
    // its configuration (Debug/Release).
    private static string FindExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("PAARDE_WINUI_EXE");
        if (!string.IsNullOrEmpty(overridePath))
        {
            return overridePath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Paarde.WinUI")))
        {
            directory = directory.Parent;
        }

        var binFolder = directory == null ? null : Path.Combine(directory.FullName, "Paarde.WinUI", "bin");
        var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}") ? "Release" : "Debug";

        var executable = binFolder == null || !Directory.Exists(binFolder)
            ? null
            : Directory.GetFiles(binFolder, "Paarde.WinUI.exe", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{Path.DirectorySeparatorChar}{configuration}{Path.DirectorySeparatorChar}"))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        return executable ?? throw new FileNotFoundException(
            $"No {configuration} build of Paarde.WinUI.exe was found. Build Paarde.Windows.slnx, or set PAARDE_WINUI_EXE.");
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(nint hwnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);
}
