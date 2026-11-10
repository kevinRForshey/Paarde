namespace Paarde.Presentation.Models;

// The one place that turns LocalSettingsOptions.ApplicationDataFolder into a full path - shared by
// LocalSettingsService, SecureStorageService, IRunSessionStore's registration and each host's logging
// setup (which runs ahead of DI and so only has the configured string). Takes the string rather than
// IConfiguration so Presentation needs no configuration package. Every front end shares one folder.
public static class ApplicationDataPaths
{
    public const string DefaultApplicationDataFolder = "Paarde/ApplicationData";

    // Where settings lived while the Avalonia app was the only front end.
    public const string LegacyApplicationDataFolder = "Paarde.Avalonia/ApplicationData";

    public static string Resolve(string? configuredFolder)
    {
        var folder = configuredFolder ?? DefaultApplicationDataFolder;

        // Until MoveLegacyFolder succeeds (it can't while an older build holds a log file open), keep
        // reading the legacy folder rather than starting with empty settings.
        if (IsDefault(folder) && !Directory.Exists(FullPath(DefaultApplicationDataFolder)) && Directory.Exists(FullPath(LegacyApplicationDataFolder)))
        {
            folder = LegacyApplicationDataFolder;
        }

        return FullPath(folder);
    }

    // Called by each host at startup, before anything opens a file in the folder. Only moves the
    // legacy folder when the default folder is in use, so a configured test folder leaves it alone.
    public static void MoveLegacyFolder(string? configuredFolder)
    {
        if (!IsDefault(configuredFolder ?? DefaultApplicationDataFolder))
        {
            return;
        }

        var legacyPath = FullPath(LegacyApplicationDataFolder);
        var path = FullPath(DefaultApplicationDataFolder);

        if (!Directory.Exists(legacyPath) || Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            Directory.Move(legacyPath, path);
        }
        catch (IOException)
        {
            // In use; Resolve keeps using the legacy folder and the move is retried next start.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsDefault(string folder) =>
        string.Equals(folder.Replace('\\', '/').TrimEnd('/'), DefaultApplicationDataFolder, StringComparison.OrdinalIgnoreCase);

    private static string FullPath(string folder) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folder);
}
