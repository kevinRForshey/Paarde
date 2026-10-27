using System.Text.Json;

using Paarde.Core.Contracts.Services;
using Paarde.Core.Models;

namespace Paarde.Core.Services;

// specs/build-and-run/design.md: language-specific detection knowledge as data (file
// classification rules, default command templates) rather than branching code elsewhere, so a
// new language is a new private helper here, not a change to every caller. C# detection (R2.1) is
// solid (.csproj inspection, no full MSBuild evaluation needed for just these checks);
// Python/TypeScript (R2.4/R2.5) are acknowledged first-pass heuristics, always correctable by
// hand-editing or adding a configuration (R2.6).
public class RunConfigurationDetector : IRunConfigurationDetector
{
    private static readonly string[] PythonEntryPointFileNames = ["main.py", "app.py", "manage.py"];

    public IReadOnlyList<RunConfiguration> DetectConfigurations(IReadOnlyDictionary<string, string> languageRoots)
    {
        var configurations = new List<RunConfiguration>();

        if (languageRoots.TryGetValue("csharp", out var csharpRoot))
        {
            configurations.AddRange(DetectCSharpConfigurations(csharpRoot));
        }

        if (languageRoots.TryGetValue("python", out var pythonRoot))
        {
            configurations.AddRange(DetectPythonConfigurations(pythonRoot));
        }

        if (languageRoots.TryGetValue("typescript", out var typeScriptRoot))
        {
            configurations.AddRange(DetectTypeScriptConfigurations(typeScriptRoot));
        }

        return configurations;
    }

    // R2.1/R2.2: every .csproj under the root is a candidate, pruning bin/obj/.git (same
    // "artifacts and VCS metadata don't count" convention FileTreeBuilder already applies
    // elsewhere), excluded if it looks like a test project and otherwise included if it declares
    // an Exe/WinExe OutputType or the web SDK.
    private static IEnumerable<RunConfiguration> DetectCSharpConfigurations(string csharpRoot)
    {
        foreach (var projectFilePath in EnumerateProjectFilesSafely(csharpRoot))
        {
            string projectFileContents;
            try
            {
                projectFileContents = File.ReadAllText(projectFilePath);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (IsTestProject(projectFilePath, projectFileContents) || !IsRunnableProject(projectFileContents))
            {
                continue;
            }

            var projectDirectory = Path.GetDirectoryName(projectFilePath)!;

            yield return new RunConfiguration(
                Id: Guid.NewGuid().ToString(),
                Name: Path.GetFileNameWithoutExtension(projectFilePath),
                LanguageId: "csharp",
                WorkingDirectoryPath: projectDirectory,
                ProjectRelativePath: Path.GetRelativePath(csharpRoot, projectFilePath),
                BuildCommand: $"dotnet build \"{projectFilePath}\"",
                RunCommand: $"dotnet run --project \"{projectFilePath}\"",
                IsAutoDetected: true);
        }
    }

    private static bool IsTestProject(string projectFilePath, string projectFileContents)
    {
        var fileName = Path.GetFileName(projectFilePath);

        return fileName.EndsWith(".Tests.csproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".Test.csproj", StringComparison.OrdinalIgnoreCase)
            || projectFileContents.Contains("<IsTestProject>true</IsTestProject>", StringComparison.OrdinalIgnoreCase)
            || projectFileContents.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRunnableProject(string projectFileContents) =>
        projectFileContents.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase)
        || projectFileContents.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase)
        || projectFileContents.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateProjectFilesSafely(string csharpRoot)
    {
        try
        {
            return Directory.EnumerateFiles(csharpRoot, "*.csproj", SearchOption.AllDirectories)
                .Where(path => !IsUnderIgnoredDirectory(csharpRoot, path))
                .ToList();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static readonly string[] IgnoredDirectoryNames = ["bin", "obj", ".git"];

    private static bool IsUnderIgnoredDirectory(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Any(segment => IgnoredDirectoryNames.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    // R2.4: best-effort, not recursive - zero candidates (requiring the user to add one by hand)
    // is an acceptable outcome, not a bug.
    private static IEnumerable<RunConfiguration> DetectPythonConfigurations(string pythonRoot)
    {
        foreach (var fileName in PythonEntryPointFileNames)
        {
            var filePath = Path.Combine(pythonRoot, fileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            yield return new RunConfiguration(
                Id: Guid.NewGuid().ToString(),
                Name: Path.GetFileNameWithoutExtension(fileName),
                LanguageId: "python",
                WorkingDirectoryPath: pythonRoot,
                ProjectRelativePath: fileName,
                BuildCommand: string.Empty,
                RunCommand: $"python \"{fileName}\"",
                IsAutoDetected: true);
        }
    }

    // R2.5: one candidate per root (not per script) - a dev/start entry (preferring dev) becomes
    // the run command, a build entry becomes the build command. No candidate at all if neither dev
    // nor start exists, since a build command with nothing to run isn't useful on its own.
    private static IEnumerable<RunConfiguration> DetectTypeScriptConfigurations(string typeScriptRoot)
    {
        var packageJsonPath = Path.Combine(typeScriptRoot, "package.json");
        if (!File.Exists(packageJsonPath))
        {
            yield break;
        }

        string packageJsonContents;
        try
        {
            packageJsonContents = File.ReadAllText(packageJsonPath);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(packageJsonContents).RootElement;
        }
        catch (JsonException)
        {
            yield break;
        }

        if (!root.TryGetProperty("scripts", out var scripts) || scripts.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        var hasDev = scripts.TryGetProperty("dev", out _);
        var hasStart = scripts.TryGetProperty("start", out _);
        var hasBuild = scripts.TryGetProperty("build", out _);

        if (!hasDev && !hasStart)
        {
            yield break;
        }

        var packageName = root.TryGetProperty("name", out var nameProperty) && nameProperty.ValueKind == JsonValueKind.String
            ? nameProperty.GetString()
            : null;

        yield return new RunConfiguration(
            Id: Guid.NewGuid().ToString(),
            Name: string.IsNullOrWhiteSpace(packageName) ? Path.GetFileName(typeScriptRoot) : packageName!,
            LanguageId: "typescript",
            WorkingDirectoryPath: typeScriptRoot,
            ProjectRelativePath: "package.json",
            BuildCommand: hasBuild ? "npm run build" : string.Empty,
            RunCommand: hasDev ? "npm run dev" : "npm start",
            IsAutoDetected: true);
    }
}
