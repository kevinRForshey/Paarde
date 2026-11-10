namespace Paarde.Core.Helpers;

// Reads a file's content for diffing purposes, treating "doesn't exist" and "can't be read" the same
// as "empty" rather than throwing - a missing file just means the proposed change is a brand-new
// file (the whole proposed content shows up as added lines), and a transient read failure shouldn't
// block showing a diff at all. Shared by ProposedFileChangeViewModel.RefreshOriginalContent and
// ApplyOrchestrationService's pre-write conflict check so both read "what's on disk right
// now" the same way.
public static class FileContentReader
{
    public static string ReadOrEmpty(string fullPath)
    {
        try
        {
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }
}
