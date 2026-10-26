using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

public class ProjectTypeDetectorTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly ProjectTypeDetector _detector = new();

    public ProjectTypeDetectorTests()
    {
        _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private string CreateFile(string relativePath)
    {
        var fullPath = Path.Combine(_workingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "content");
        return fullPath;
    }

    [Fact]
    public void DetectLanguageRoots_EmptyDirectory_ReturnsEmptySet()
    {
        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Empty(languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_CsprojAtRoot_ReturnsCSharp()
    {
        CreateFile("App.csproj");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(new Dictionary<string, string> { ["csharp"] = _workingDirectory }, languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_SlnxAtRoot_ReturnsCSharp()
    {
        CreateFile("App.slnx");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(new Dictionary<string, string> { ["csharp"] = _workingDirectory }, languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_RequirementsTxtAtRoot_ReturnsPython()
    {
        CreateFile("requirements.txt");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(new Dictionary<string, string> { ["python"] = _workingDirectory }, languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_PackageJsonAtRoot_ReturnsTypeScript()
    {
        CreateFile("package.json");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(new Dictionary<string, string> { ["typescript"] = _workingDirectory }, languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_MarkerInImmediateSubdirectory_RootsAtThatSubdirectory()
    {
        CreateFile("frontend/package.json");
        var expectedRoot = Path.Combine(_workingDirectory, "frontend");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(new Dictionary<string, string> { ["typescript"] = expectedRoot }, languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_MarkerTwoLevelsDeep_IsNotDetected()
    {
        CreateFile("frontend/app/package.json");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Empty(languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_PolyglotRepoWithPythonAndTypeScriptSideBySide_RootsEachAtItsOwnSubdirectory()
    {
        CreateFile("backend/requirements.txt");
        CreateFile("frontend/package.json");
        var expectedBackendRoot = Path.Combine(_workingDirectory, "backend");
        var expectedFrontendRoot = Path.Combine(_workingDirectory, "frontend");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Equal(
            new Dictionary<string, string> { ["python"] = expectedBackendRoot, ["typescript"] = expectedFrontendRoot },
            languageRoots);
    }

    [Fact]
    public void DetectLanguageRoots_GitDirectory_IsIgnored()
    {
        CreateFile(".git/package.json");

        var languageRoots = _detector.DetectLanguageRoots(_workingDirectory);

        Assert.Empty(languageRoots);
    }
}
