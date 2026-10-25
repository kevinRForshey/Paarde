namespace Paarde.Core.Contracts.Services;

// Epic 18 (specs/multi-language-support R4): decides which language servers are even worth
// attempting for a working directory, so opening a pure C# repo never tries (and fails, noisily)
// to launch a Python or TypeScript server, and vice versa.
public interface IProjectTypeDetector
{
    // Returned strings are LanguageServerOptions.LanguageId values ("csharp"/"python"/
    // "typescript"), not LSP languageIds - this is "which configured servers should run here," not
    // "what language is this one file."
    IReadOnlySet<string> DetectLanguageIds(string workingDirectoryPath);
}
