namespace Paarde.Core.Models;

// One file or directory in a FileTreeBuilder.Build() result. Children is empty (not
// null) for a file, and for a directory with no visible (non-gitignored) entries, so a consumer
// never has to null-check before enumerating it.
public sealed record FileTreeEntry(string Name, string FullPath, bool IsDirectory, IReadOnlyList<FileTreeEntry> Children);
