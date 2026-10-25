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
    public void DetectLanguageIds_EmptyDirectory_ReturnsEmptySet()
    {
        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Empty(languageIds);
    }

    [Fact]
    public void DetectLanguageIds_CsprojAtRoot_ReturnsCSharp()
    {
        CreateFile("App.csproj");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new[] { "csharp" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_SlnxAtRoot_ReturnsCSharp()
    {
        CreateFile("App.slnx");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new[] { "csharp" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_RequirementsTxtAtRoot_ReturnsPython()
    {
        CreateFile("requirements.txt");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new[] { "python" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_PackageJsonAtRoot_ReturnsTypeScript()
    {
        CreateFile("package.json");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new[] { "typescript" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_MarkerInImmediateSubdirectory_IsDetected()
    {
        CreateFile("frontend/package.json");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new[] { "typescript" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_MarkerTwoLevelsDeep_IsNotDetected()
    {
        CreateFile("frontend/app/package.json");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Empty(languageIds);
    }

    [Fact]
    public void DetectLanguageIds_PolyglotRepoWithPythonAndTypeScriptSideBySide_ReturnsBoth()
    {
        CreateFile("backend/requirements.txt");
        CreateFile("frontend/package.json");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Equal(new HashSet<string> { "python", "typescript" }, languageIds);
    }

    [Fact]
    public void DetectLanguageIds_GitDirectory_IsIgnored()
    {
        CreateFile(".git/package.json");

        var languageIds = _detector.DetectLanguageIds(_workingDirectory);

        Assert.Empty(languageIds);
    }
}
