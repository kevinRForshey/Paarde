namespace Paarde.Core.Models;

// Epic 20 (specs/build-and-run): the per-configuration counterpart to Epic 18's
// LanguageServerOptions - a working directory can contain more than one runnable target (an API
// project and a console tool side by side), so "what Build/Run means" is one of these, not a
// single flat command pair.
//
// WorkingDirectoryPath is the *project's own* directory (e.g. a .csproj's folder), not the
// overall repo working directory passed to IProjectTypeDetector - `dotnet run --project <path>`
// (and an interpreted script's relative imports) resolve relative paths against that directory,
// not an ancestor. ProjectRelativePath is relative to the detected language root instead, since
// it (together with LanguageId) is what RunConfigurationsProvider uses to recognize "the same
// target" across repeated detection passes (R2.3) - a path relative to the target's own directory
// would always just be its filename and couldn't do that.
public record RunConfiguration(
    string Id,
    string Name,
    string LanguageId,
    string WorkingDirectoryPath,
    string ProjectRelativePath,
    string BuildCommand,
    string RunCommand,
    bool IsAutoDetected);
