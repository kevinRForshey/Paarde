using Paarde.Presentation.Models;

namespace Paarde.Presentation.Tests.Models;

// Runs against a temporary AppData root, never the real %AppData%.
public class ApplicationDataPathsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("PaardeAppData_").FullName;

    private string LegacyPath => Path.Combine(_root, ApplicationDataPaths.LegacyApplicationDataFolder);

    private string NewPath => Path.Combine(_root, ApplicationDataPaths.DefaultApplicationDataFolder);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private void CreateLegacySettings()
    {
        Directory.CreateDirectory(LegacyPath);
        File.WriteAllText(Path.Combine(LegacyPath, "LocalSettings.json"), "{}");
    }

    [Fact]
    public void MoveLegacyFolder_OnlyLegacyExists_MovesItAndResolveUsesTheNewFolder()
    {
        CreateLegacySettings();

        ApplicationDataPaths.MoveLegacyFolder(ApplicationDataPaths.DefaultApplicationDataFolder, _root);

        Assert.True(File.Exists(Path.Combine(NewPath, "LocalSettings.json")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(LegacyPath)));
        Assert.Equal(NewPath, ApplicationDataPaths.Resolve(ApplicationDataPaths.DefaultApplicationDataFolder, _root));
    }

    [Fact]
    public void MoveLegacyFolder_NewFolderAlreadyExists_LeavesBothAlone()
    {
        CreateLegacySettings();
        Directory.CreateDirectory(NewPath);

        ApplicationDataPaths.MoveLegacyFolder(null, _root);

        Assert.True(File.Exists(Path.Combine(LegacyPath, "LocalSettings.json")));
        Assert.Equal(NewPath, ApplicationDataPaths.Resolve(null, _root));
    }

    [Fact]
    public void MoveLegacyFolder_CustomFolderConfigured_DoesNotTouchTheLegacyFolder()
    {
        CreateLegacySettings();

        ApplicationDataPaths.MoveLegacyFolder(Path.Combine(_root, "custom"), _root);

        Assert.True(Directory.Exists(LegacyPath));
        Assert.False(Directory.Exists(NewPath));
    }

    [Fact]
    public void Resolve_LegacyFolderCouldNotBeMoved_KeepsUsingIt()
    {
        CreateLegacySettings();

        Assert.Equal(LegacyPath, ApplicationDataPaths.Resolve(ApplicationDataPaths.DefaultApplicationDataFolder, _root));
    }

    [Fact]
    public void MoveLegacyFolder_AFileInItIsOpen_LeavesItForTheNextStart()
    {
        CreateLegacySettings();
        using (File.Open(Path.Combine(LegacyPath, "LocalSettings.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ApplicationDataPaths.MoveLegacyFolder(null, _root);
        }

        Assert.True(Directory.Exists(LegacyPath));
        Assert.Equal(LegacyPath, ApplicationDataPaths.Resolve(null, _root));
    }
}
