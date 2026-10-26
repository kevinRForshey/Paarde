namespace Paarde.Core.Contracts.Services;

// Epic 18 (specs/multi-language-support R4): decides which language servers are even worth
// attempting for a working directory, so opening a pure C# repo never tries (and fails, noisily)
// to launch a Python or TypeScript server, and vice versa.
public interface IProjectTypeDetector
{
    // Keys are LanguageServerOptions.LanguageId values ("csharp"/"python"/"typescript"), not LSP
    // languageIds. Values are the directory where that language's marker file was found - a
    // server must be rooted there (its rootUri), not at workingDirectoryPath, because
    // typescript-language-server (and other servers) resolve their project/toolchain from rootUri
    // and a polyglot repo's backend/ and frontend/ projects can each have their own.
    IReadOnlyDictionary<string, string> DetectLanguageRoots(string workingDirectoryPath);
}
