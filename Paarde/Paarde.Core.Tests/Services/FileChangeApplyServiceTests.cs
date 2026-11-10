using System.Text;

using Paarde.Core.Models;
using Paarde.Core.Services;
using Paarde.Core.ViewModels;

namespace Paarde.Core.Tests.Services;

public class FileChangeApplyServiceTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly FileChangeApplyService _service = new();

    public FileChangeApplyServiceTests()
    {
        _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    }

    public void Dispose()
    {
        if (!Directory.Exists(_workingDirectory))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            // Restore write access in case the permission-failure test locked it down, so cleanup
            // doesn't itself throw.
            File.SetUnixFileMode(_workingDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Directory.Delete(_workingDirectory, recursive: true);
    }

    [Fact]
    public void Apply_NoWorkingDirectory_Throws()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "hi");

        Assert.Throws<InvalidOperationException>(() => _service.Apply(file, string.Empty));
    }

    [Fact]
    public void Apply_NewFile_WritesContentToWorkingDirectory()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "hello world");

        _service.Apply(file, _workingDirectory);

        var written = Path.Combine(_workingDirectory, "a.txt");
        Assert.True(File.Exists(written));
        Assert.Equal("hello world", File.ReadAllText(written));
    }

    [Fact]
    public void Apply_NestedPath_CreatesIntermediateDirectories()
    {
        var file = new ProposedFileChangeViewModel(Path.Combine("src", "nested", "Foo.cs"), "class Foo {}");

        _service.Apply(file, _workingDirectory);

        var written = Path.Combine(_workingDirectory, "src", "nested", "Foo.cs");
        Assert.True(File.Exists(written));
        Assert.Equal("class Foo {}", File.ReadAllText(written));
    }

    [Fact]
    public void Apply_OverwritesExistingFile()
    {
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "old content");
        var file = new ProposedFileChangeViewModel("a.txt", "new content");

        _service.Apply(file, _workingDirectory);

        Assert.Equal("new content", File.ReadAllText(path));
    }

    [Fact]
    public void Apply_UserEditedContent_WritesEditedContentNotProposedContent()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "proposed content")
        {
            EditedContent = "user-edited content",
        };

        _service.Apply(file, _workingDirectory);

        var written = Path.Combine(_workingDirectory, "a.txt");
        Assert.Equal("user-edited content", File.ReadAllText(written));
    }

    [Fact]
    public void Apply_StepDeselected_WritesOriginalLinesForThatHunkInstead()
    {
        var hunk = new ProposedHunk("Add line2", string.Empty, "line2", new HunkLocation(1, 1))
        {
            IsSelected = false,
        };
        var file = new ProposedFileChangeViewModel("a.txt", "line1\nline2", "line1") { Hunks = new[] { hunk } };

        _service.Apply(file, _workingDirectory);

        var written = Path.Combine(_workingDirectory, "a.txt");
        Assert.Equal("line1", File.ReadAllText(written));
    }

    [Fact]
    public void Apply_SuccessfulWrite_MarksHunksAppliedFromCurrentSelection()
    {
        var selected = new ProposedHunk("Step 1", string.Empty, "x", null);
        var deselected = new ProposedHunk("Step 2", string.Empty, "y", null) { IsSelected = false };
        var file = new ProposedFileChangeViewModel("a.txt", "proposed", "original")
        {
            Hunks = new[] { selected, deselected },
        };

        _service.Apply(file, _workingDirectory);

        Assert.True(selected.IsApplied);
        Assert.False(deselected.IsApplied);
    }

    // The file this write overwrites already uses CRLF, but DiffBuilder.ComposeContent joins
    // hunk-based content with bare '\n' (see DiffBuilder.cs) - without normalizing back to the
    // original's convention, a deselected-hunk write like this would silently flip the whole file
    // to LF even though only one line's content actually changed.
    [Fact]
    public void Apply_ExistingCrlfFile_WritesComposedContentWithCrlfNotLf()
    {
        // The file already on disk uses CRLF. DiffBuilder.ComposeContent (what BuildSelectedContent
        // calls for a file with resolved hunks) always joins with bare '\n' regardless - without
        // normalizing back to CRLF here, this deselected-line3 write would flip the file to LF even
        // though line1/line2 themselves never changed.
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "some\r\nexisting\r\ncontent", new UTF8Encoding(false));
        var hunk = new ProposedHunk("Add line3", string.Empty, "line3", new HunkLocation(2, 2)) { IsSelected = false };
        var file = new ProposedFileChangeViewModel("a.txt", "line1\nline2\nline3", "line1\nline2") { Hunks = new[] { hunk } };

        _service.Apply(file, _workingDirectory);

        Assert.Equal("line1\r\nline2", File.ReadAllText(path));
    }

    // Mirrors Apply_ExistingCrlfFile_WritesComposedContentWithCrlfNotLf but for a file that's
    // already LF - exercises NormalizeNewLines' other branch (target newLine == "\n", a no-op
    // replace) rather than only ever hitting the CRLF expansion path.
    [Fact]
    public void Apply_ExistingLfFile_WritesComposedContentWithLf()
    {
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "some\nexisting\ncontent", new UTF8Encoding(false));
        var hunk = new ProposedHunk("Add line3", string.Empty, "line3", new HunkLocation(2, 2)) { IsSelected = false };
        var file = new ProposedFileChangeViewModel("a.txt", "line1\nline2\nline3", "line1\nline2") { Hunks = new[] { hunk } };

        _service.Apply(file, _workingDirectory);

        Assert.Equal("line1\nline2", File.ReadAllText(path));
    }

    [Fact]
    public void Apply_ExistingFileWithUtf8Bom_PreservesBomOnWrite()
    {
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "old content", new UTF8Encoding(true));
        var file = new ProposedFileChangeViewModel("a.txt", "new content");

        _service.Apply(file, _workingDirectory);

        var writtenBytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, writtenBytes[0]);
        Assert.Equal(0xBB, writtenBytes[1]);
        Assert.Equal(0xBF, writtenBytes[2]);
        Assert.Equal("new content", File.ReadAllText(path));
    }

    [Fact]
    public void Apply_NewFile_WritesUtf8WithoutBom()
    {
        var file = new ProposedFileChangeViewModel("a.txt", "hello world");

        _service.Apply(file, _workingDirectory);

        var writtenBytes = File.ReadAllBytes(Path.Combine(_workingDirectory, "a.txt"));
        Assert.False(writtenBytes.Length >= 3 && writtenBytes[0] == 0xEF && writtenBytes[1] == 0xBB && writtenBytes[2] == 0xBF);
    }

    [Fact]
    public void Apply_WholeFileFallbackOnCrlfFile_NormalizesEditedContentToCrlf()
    {
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "old1\r\nold2", new UTF8Encoding(false));

        // EditedContent only has '\n' - as if the user typed it fresh in the editor - with no hunks
        // resolved, so this exercises the other BuildSelectedContent branch (plain EditedContent,
        // not DiffBuilder.ComposeContent).
        var file = new ProposedFileChangeViewModel("a.txt", "new1\nnew2") { EditedContent = "new1\nnew2" };

        _service.Apply(file, _workingDirectory);

        Assert.Equal("new1\r\nnew2", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../../../etc/escape.txt")]
    [InlineData("nested/../../escape.txt")]
    public void Apply_RelativePathTraversal_ThrowsAndDoesNotWrite(string traversalPath)
    {
        var file = new ProposedFileChangeViewModel(traversalPath, "malicious");

        Assert.Throws<InvalidOperationException>(() => _service.Apply(file, _workingDirectory));

        var escapedTarget = Path.GetFullPath(Path.Combine(_workingDirectory, traversalPath));
        Assert.False(File.Exists(escapedTarget));
    }

    [Fact]
    public void Apply_AbsolutePath_ThrowsAndDoesNotWrite()
    {
        var outsideDirectory = Directory.CreateTempSubdirectory("PaardeTests_outside_").FullName;
        try
        {
            var absoluteTarget = Path.Combine(outsideDirectory, "escape.txt");
            var file = new ProposedFileChangeViewModel(absoluteTarget, "malicious");

            Assert.Throws<InvalidOperationException>(() => _service.Apply(file, _workingDirectory));

            Assert.False(File.Exists(absoluteTarget));
        }
        finally
        {
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public void Apply_DestinationDirectoryNotWritable_ThrowsIOOrUnauthorizedException()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            // Windows ACLs and root both make a reliable "permission denied" repro impractical here;
            // this path is covered on a normal Unix CI/dev user.
            return;
        }

        File.SetUnixFileMode(_workingDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var file = new ProposedFileChangeViewModel("a.txt", "hi");

        Assert.ThrowsAny<UnauthorizedAccessException>(() => _service.Apply(file, _workingDirectory));
    }

    // WriteRaw is Apply's underlying write path with no ProposedFileChangeViewModel
    // involved - these exercise it directly as its own public contract, covering the same
    // path-containment and encoding-preservation behavior the Apply_* tests above already cover
    // transitively through it.
    [Fact]
    public void WriteRaw_NewFile_WritesContentToWorkingDirectory()
    {
        _service.WriteRaw("a.txt", "hello world", _workingDirectory);

        var written = Path.Combine(_workingDirectory, "a.txt");
        Assert.True(File.Exists(written));
        Assert.Equal("hello world", File.ReadAllText(written));
    }

    [Fact]
    public void WriteRaw_ExistingFile_OverwritesItPreservingEncoding()
    {
        var path = Path.Combine(_workingDirectory, "a.txt");
        File.WriteAllText(path, "old content", new UTF8Encoding(true));

        _service.WriteRaw("a.txt", "restored content", _workingDirectory);

        var writtenBytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, writtenBytes[0]);
        Assert.Equal(0xBB, writtenBytes[1]);
        Assert.Equal(0xBF, writtenBytes[2]);
        Assert.Equal("restored content", File.ReadAllText(path));
    }

    [Fact]
    public void WriteRaw_PathTraversal_ThrowsAndDoesNotWrite()
    {
        Assert.Throws<InvalidOperationException>(() => _service.WriteRaw("../escape.txt", "malicious", _workingDirectory));

        var escapedTarget = Path.GetFullPath(Path.Combine(_workingDirectory, "../escape.txt"));
        Assert.False(File.Exists(escapedTarget));
    }
}
