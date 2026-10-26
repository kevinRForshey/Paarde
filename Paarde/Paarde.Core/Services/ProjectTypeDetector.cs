using Paarde.Core.Contracts.Services;

namespace Paarde.Core.Services;

// specs/multi-language-support/design.md: scans the working directory itself plus its immediate
// subdirectories (depth 0 and depth 1) for each language's conventional marker file(s) - covers
// the common "backend/ + frontend/" polyglot-repo split without a full recursive walk, which isn't
// needed since these markers conventionally live at a project's own root.
public class ProjectTypeDetector : IProjectTypeDetector
{
    private static readonly string[] CSharpMarkerExtensions = [".csproj", ".sln", ".slnx"];
    private static readonly string[] PythonMarkerFileNames = ["pyproject.toml", "setup.py", "requirements.txt"];
    private static readonly string[] TypeScriptMarkerFileNames = ["package.json"];

    public IReadOnlyDictionary<string, string> DetectLanguageRoots(string workingDirectoryPath)
    {
        var languageRoots = new Dictionary<string, string>(StringComparer.Ordinal);

        // Root is scanned before subdirectories, and DetectInDirectory only ever adds a language
        // that isn't already present, so a root-level marker always wins over one found later in a
        // subdirectory - same first-found-wins precedence the previous flat-HashSet version had.
        DetectInDirectory(workingDirectoryPath, workingDirectoryPath, languageRoots);

        foreach (var subdirectory in EnumerateDirectoriesSafely(workingDirectoryPath))
        {
            DetectInDirectory(subdirectory, subdirectory, languageRoots);
        }

        return languageRoots;
    }

    private static void DetectInDirectory(string directoryPath, string languageRootPath, Dictionary<string, string> languageRoots)
    {
        var fileNames = EnumerateFileNamesSafely(directoryPath);

        if (fileNames.Any(fileName => CSharpMarkerExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase)))
        {
            languageRoots.TryAdd("csharp", languageRootPath);
        }

        if (fileNames.Any(fileName => PythonMarkerFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)))
        {
            languageRoots.TryAdd("python", languageRootPath);
        }

        if (fileNames.Any(fileName => TypeScriptMarkerFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)))
        {
            languageRoots.TryAdd("typescript", languageRootPath);
        }
    }

    // Same "swallow and treat as empty" convention FileTreeBuilder already uses for an
    // unreadable/missing directory - detection degrading to "no markers found" is harmless here,
    // unlike a file-tree failing to render.
    private static IReadOnlyList<string> EnumerateFileNamesSafely(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath).Select(Path.GetFileName).Where(name => name is not null).Select(name => name!).ToList();
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

    private static IReadOnlyList<string> EnumerateDirectoriesSafely(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(directoryPath).Where(path => Path.GetFileName(path) != ".git").ToList();
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
}
