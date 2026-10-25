namespace Paarde.Core.Models;

// FilePath is an absolute path (converted from the server's file:// URI at the client boundary) -
// callers resolve it against Explorer's own file-open path (SelectedNode), not against a URI.
public record LspLocation(string FilePath, LspPosition Position);
