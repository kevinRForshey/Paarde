namespace Paarde.Presentation.Models;

// specs/winui-frontend R1.4: the one place that turns LocalSettingsOptions.ApplicationDataFolder into
// a full path - shared by LocalSettingsService, IRunSessionStore's registration and each host's
// logging setup (which runs ahead of DI and so only has the configured string). Takes the string
// rather than IConfiguration so Presentation needs no configuration package. The default keeps the
// "Paarde.Avalonia" name so existing settings survive, and so every front end shares one folder.
public static class ApplicationDataPaths
{
    public const string DefaultApplicationDataFolder = "Paarde.Avalonia/ApplicationData";

    public static string Resolve(string? configuredFolder) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), configuredFolder ?? DefaultApplicationDataFolder);
}
