namespace Paarde.Core.Tests.TestSupport;

public static class TempDirectory
{
    // Directory.Delete refuses read-only files on Windows, and git writes its object files
    // read-only, so a temp folder holding a test repository needs its attributes cleared first.
    public static void Delete(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
