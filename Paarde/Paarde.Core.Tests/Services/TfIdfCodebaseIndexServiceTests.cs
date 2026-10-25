using Paarde.Core.Services;

namespace Paarde.Core.Tests.Services;

public class TfIdfCodebaseIndexServiceTests : IDisposable
{
    private readonly string _workingDirectory;
    private readonly TfIdfCodebaseIndexService _sut = new();

    public TfIdfCodebaseIndexServiceTests()
    {
        _workingDirectory = Directory.CreateTempSubdirectory("PaardeTests_").FullName;
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_workingDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    [Fact]
    public async Task FindRelevantFilesAsync_QueryTermAppearsMoreOftenInOneFile_RanksThatFileFirst()
    {
        CreateFile("Foo.cs", "class Foo { void Widget() { Widget(); Widget(); } }");
        CreateFile("Bar.cs", "class Bar { void Widget() { } }");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "widget");

        Assert.Equal(2, matches.Count);
        Assert.Equal("Foo.cs", matches[0].RelativePath);
        Assert.Equal("Bar.cs", matches[1].RelativePath);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_QueryTermNotPresentInAnyFile_ReturnsEmpty()
    {
        CreateFile("Foo.cs", "class Foo {}");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "nonexistentterm");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_BlankQuery_ReturnsEmpty()
    {
        CreateFile("Foo.cs", "class Foo {}");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "   ");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_WorkingDirectoryDoesNotExist_ReturnsEmpty()
    {
        var matches = await _sut.FindRelevantFilesAsync(Path.Combine(_workingDirectory, "missing"), "widget");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_ResultPaths_AreRelativeToTheWorkingDirectory()
    {
        CreateFile("src/Foo.cs", "class Foo { void Widget() {} }");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "widget");

        Assert.Equal("src/Foo.cs", Assert.Single(matches).RelativePath);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_FileContainsANulByteNearTheStart_IsSkippedAsBinary()
    {
        var bytes = new byte[] { (byte)'u', (byte)'n', (byte)'i', (byte)'q', (byte)'u', (byte)'e', 0, 1, 2, 3 };
        File.WriteAllBytes(Path.Combine(_workingDirectory, "data.bin"), bytes);
        CreateFile("Foo.cs", "class Foo {}");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "unique");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_FileContentChangesBetweenCalls_ReflectsTheNewContent()
    {
        var path = CreateFile("Foo.cs", "class Foo { void Alpha() {} }");

        var firstCall = await _sut.FindRelevantFilesAsync(_workingDirectory, "alpha");
        Assert.Single(firstCall);

        File.WriteAllText(path, "class Foo { void Beta() {} }");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var afterAlphaRemoved = await _sut.FindRelevantFilesAsync(_workingDirectory, "alpha");
        Assert.Empty(afterAlphaRemoved);

        var afterBetaAdded = await _sut.FindRelevantFilesAsync(_workingDirectory, "beta");
        Assert.Single(afterBetaAdded);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_FileDeletedBetweenCalls_IsRemovedFromResults()
    {
        var path = CreateFile("Foo.cs", "class Foo { void Alpha() {} }");
        CreateFile("Bar.cs", "class Bar {}");

        var firstCall = await _sut.FindRelevantFilesAsync(_workingDirectory, "alpha");
        Assert.Single(firstCall);

        File.Delete(path);

        var afterDelete = await _sut.FindRelevantFilesAsync(_workingDirectory, "alpha");
        Assert.Empty(afterDelete);
    }

    [Fact]
    public async Task FindRelevantFilesAsync_MaxResults_CapsTheReturnedCount()
    {
        CreateFile("A.cs", "widget");
        CreateFile("B.cs", "widget");
        CreateFile("C.cs", "widget");

        var matches = await _sut.FindRelevantFilesAsync(_workingDirectory, "widget", maxResults: 2);

        Assert.Equal(2, matches.Count);
    }
}
