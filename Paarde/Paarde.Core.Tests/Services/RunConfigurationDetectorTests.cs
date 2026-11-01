using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

public class RunConfigurationDetectorTests : IDisposable
{
    private readonly string _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    private readonly RunConfigurationDetector _detector = new();

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string contents = "")
    {
        var fullPath = Path.Combine(_workingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, contents);
        return fullPath;
    }

    private const string ConsoleAppCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>";
    private const string WebAppCsproj = "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>";
    private const string ClassLibraryCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>";
    private const string TestProjectCsproj = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>";

    [Fact]
    public void DetectConfigurations_NoLanguageRoots_ReturnsEmpty()
    {
        var configurations = _detector.DetectConfigurations(new Dictionary<string, string>());

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_ExeCsproj_IsIncluded()
    {
        CreateFile("App.csproj", ConsoleAppCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        var configuration = Assert.Single(configurations);
        Assert.Equal("csharp", configuration.LanguageId);
        Assert.Equal("App", configuration.Name);
        Assert.True(configuration.IsAutoDetected);
        Assert.Contains("App.csproj", configuration.BuildCommand);
        Assert.Contains("dotnet run", configuration.RunCommand);
    }

    [Fact]
    public void DetectConfigurations_WebSdkCsproj_IsIncludedEvenWithoutExplicitOutputType()
    {
        CreateFile("Api.csproj", WebAppCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        Assert.Single(configurations);
    }

    [Fact]
    public void DetectConfigurations_ClassLibraryCsproj_IsExcluded()
    {
        CreateFile("Lib.csproj", ClassLibraryCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_TestSdkPackageReference_IsExcludedEvenWithExeOutputType()
    {
        CreateFile("App.Tests.csproj", TestProjectCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_TestsCsprojFileNameSuffix_IsExcluded()
    {
        CreateFile("Widgets.Tests.csproj", ConsoleAppCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_CsprojUnderBinOrObj_IsIgnored()
    {
        CreateFile(Path.Combine("bin", "Debug", "Copy.csproj"), ConsoleAppCsproj);
        CreateFile(Path.Combine("obj", "Copy.csproj"), ConsoleAppCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_CsprojNestedUnderRoot_WorkingDirectoryIsTheProjectsOwnDirectory()
    {
        var projectPath = CreateFile(Path.Combine("src", "App", "App.csproj"), ConsoleAppCsproj);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["csharp"] = _workingDirectory });

        var configuration = Assert.Single(configurations);
        Assert.Equal(Path.GetDirectoryName(projectPath), configuration.WorkingDirectoryPath);
        Assert.Equal(Path.Combine("src", "App", "App.csproj"), configuration.ProjectRelativePath);
    }

    [Theory]
    [InlineData("main.py")]
    [InlineData("app.py")]
    [InlineData("manage.py")]
    public void DetectConfigurations_PythonEntryPointFileName_ProducesACandidate(string fileName)
    {
        CreateFile(fileName);

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["python"] = _workingDirectory });

        var configuration = Assert.Single(configurations);
        Assert.Equal("python", configuration.LanguageId);
        Assert.Empty(configuration.BuildCommand);
        Assert.Contains(fileName, configuration.RunCommand);
    }

    [Fact]
    public void DetectConfigurations_NoPythonEntryPointFiles_ProducesNoCandidates()
    {
        CreateFile("utils.py");

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["python"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_PackageJsonWithDevScript_PrefersDevOverStart()
    {
        CreateFile("package.json", "{\"name\":\"my-app\",\"scripts\":{\"dev\":\"vite\",\"start\":\"node server.js\",\"build\":\"vite build\"}}");

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["typescript"] = _workingDirectory });

        var configuration = Assert.Single(configurations);
        Assert.Equal("typescript", configuration.LanguageId);
        Assert.Equal("my-app", configuration.Name);
        Assert.Equal("npm run dev", configuration.RunCommand);
        Assert.Equal("npm run build", configuration.BuildCommand);
    }

    [Fact]
    public void DetectConfigurations_PackageJsonWithOnlyStartScript_UsesStart()
    {
        CreateFile("package.json", "{\"scripts\":{\"start\":\"node server.js\"}}");

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["typescript"] = _workingDirectory });

        var configuration = Assert.Single(configurations);
        Assert.Equal("npm start", configuration.RunCommand);
        Assert.Empty(configuration.BuildCommand);
    }

    [Fact]
    public void DetectConfigurations_PackageJsonWithNeitherDevNorStart_ProducesNoCandidate()
    {
        CreateFile("package.json", "{\"scripts\":{\"build\":\"tsc\"}}");

        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["typescript"] = _workingDirectory });

        Assert.Empty(configurations);
    }

    [Fact]
    public void DetectConfigurations_NoPackageJson_ProducesNoCandidate()
    {
        var configurations = _detector.DetectConfigurations(new Dictionary<string, string> { ["typescript"] = _workingDirectory });

        Assert.Empty(configurations);
    }
}
