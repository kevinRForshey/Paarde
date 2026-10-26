using System.Runtime.InteropServices;

using Paarde.Core.Helpers;

namespace Paarde.Core.Tests.Helpers;

public class ExecutablePathResolverTests
{
    [Fact]
    public void ExistsOnPath_ExecutableInOneOfSeveralDirectories_ReturnsTrue()
    {
        var emptyDirectory = Directory.CreateTempSubdirectory();
        var executableDirectory = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(executableDirectory.FullName, "mytool"), string.Empty);
        var pathVariable = string.Join(Path.PathSeparator, emptyDirectory.FullName, executableDirectory.FullName);

        try
        {
            Assert.True(ExecutablePathResolver.ExistsOnPath("mytool", pathVariable));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExistsOnPath_ExecutableNotInAnyDirectory_ReturnsFalse()
    {
        var emptyDirectory = Directory.CreateTempSubdirectory();

        try
        {
            Assert.False(ExecutablePathResolver.ExistsOnPath("mytool", emptyDirectory.FullName));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExistsOnPath_EmptyPathVariable_ReturnsFalse()
    {
        Assert.False(ExecutablePathResolver.ExistsOnPath("mytool", string.Empty));
    }

    [Fact]
    public void ExistsOnPath_PathVariableWithBlankEntries_SkipsThemWithoutThrowing()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(executableDirectory.FullName, "mytool"), string.Empty);
        var pathVariable = string.Join(Path.PathSeparator, string.Empty, executableDirectory.FullName, string.Empty);

        try
        {
            Assert.True(ExecutablePathResolver.ExistsOnPath("mytool", pathVariable));
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExistsOnPath_AbsolutePathToExistingFile_ReturnsTrueWithoutConsultingPath()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "mytool");
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            Assert.True(ExecutablePathResolver.ExistsOnPath(executablePath, pathVariable: string.Empty));
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ExistsOnPath_AbsolutePathToMissingFile_ReturnsFalse()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "mytool");

        try
        {
            Assert.False(ExecutablePathResolver.ExistsOnPath(executablePath));
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveFullPath_ExecutableOnPath_ReturnsTheMatchedPath()
    {
        var executableDirectory = Directory.CreateTempSubdirectory();
        var executablePath = Path.Combine(executableDirectory.FullName, "mytool");
        File.WriteAllText(executablePath, string.Empty);

        try
        {
            Assert.Equal(executablePath, ExecutablePathResolver.ResolveFullPath("mytool", executableDirectory.FullName));
        }
        finally
        {
            executableDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ResolveFullPath_ExecutableNotFound_ReturnsNull()
    {
        var emptyDirectory = Directory.CreateTempSubdirectory();

        try
        {
            Assert.Null(ExecutablePathResolver.ResolveFullPath("mytool", emptyDirectory.FullName));
        }
        finally
        {
            emptyDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void RequiresShellWrapper_CmdExtension_ReturnsTrueOnWindowsOnly()
    {
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), ExecutablePathResolver.RequiresShellWrapper(@"C:\tools\server.cmd"));
    }

    [Fact]
    public void RequiresShellWrapper_BatExtension_ReturnsTrueOnWindowsOnly()
    {
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), ExecutablePathResolver.RequiresShellWrapper(@"C:\tools\server.bat"));
    }

    [Fact]
    public void RequiresShellWrapper_ExeExtension_ReturnsFalse()
    {
        Assert.False(ExecutablePathResolver.RequiresShellWrapper(@"C:\tools\server.exe"));
    }

    [Fact]
    public void RequiresShellWrapper_CmdExtensionCasingVariant_StillMatches()
    {
        Assert.Equal(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), ExecutablePathResolver.RequiresShellWrapper(@"C:\tools\server.CMD"));
    }
}
