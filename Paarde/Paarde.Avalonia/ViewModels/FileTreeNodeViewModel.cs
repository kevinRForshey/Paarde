using Paarde.Core.Models;

namespace Paarde.Avalonia.ViewModels;

// T8.1.2: pairs a FileTreeEntry (Core, pure fs structure) with IsTouched - whether this file is
// part of the current proposal in IPromptResultStore. Kept Avalonia-side rather than on
// FileTreeEntry itself since "touched by the current proposal" is UI/session state, not
// something inherent to a file on disk.
public sealed record FileTreeNodeViewModel(string Name, string FullPath, bool IsDirectory, bool IsTouched, IReadOnlyList<FileTreeNodeViewModel> Children)
{
    public static FileTreeNodeViewModel FromEntry(FileTreeEntry entry, IReadOnlySet<string> touchedFullPaths)
    {
        var children = entry.Children.Select(child => FromEntry(child, touchedFullPaths)).ToArray();

        return new FileTreeNodeViewModel(entry.Name, entry.FullPath, entry.IsDirectory, touchedFullPaths.Contains(entry.FullPath), children);
    }

    // Epic 17, T17.1.3: resolves a go-to-definition/find-references target (an absolute path from
    // the language server) back to its node in the already-built tree, so selecting it can reuse
    // ExplorerViewModel's existing SelectedNode -> LoadNode flow instead of a separate file-open path.
    public static FileTreeNodeViewModel? FindByPath(FileTreeNodeViewModel node, string fullPath)
    {
        if (string.Equals(node.FullPath, fullPath, StringComparison.Ordinal))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (FindByPath(child, fullPath) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
