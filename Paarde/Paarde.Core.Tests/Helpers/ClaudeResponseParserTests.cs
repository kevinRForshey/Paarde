using Paarde.Core.Helpers;
using Paarde.Core.Models;

namespace Paarde.Core.Tests.Helpers;

public class ClaudeResponseParserTests
{
    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyTutorialAndNoFiles()
    {
        var result = ClaudeResponseParser.Parse(string.Empty);

        Assert.Equal(string.Empty, result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_WhitespaceOnlyInput_ReturnsEmptyTutorialAndNoFiles()
    {
        var result = ClaudeResponseParser.Parse("   \n  \n ");

        Assert.Equal(string.Empty, result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_UnstructuredResponse_ReturnsTrimmedTextAsTutorialWithNoFiles()
    {
        const string cliOutput = "  This is a plain answer to a general question with no file changes.  ";

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal(cliOutput.Trim(), result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_FilesHeaderBeforeTutorialHeader_IsTreatedAsUnstructured()
    {
        const string cliOutput = "## Files\nsome text\n## Tutorial\nmore text";

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal(cliOutput.Trim(), result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_TutorialHeaderWithoutFilesHeader_IsTreatedAsUnstructured()
    {
        const string cliOutput = "## Tutorial\nSome overview text with no files section.";

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal(cliOutput.Trim(), result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_HeadersPresentButNoFileEntries_ReturnsTutorialWithNoFiles()
    {
        const string cliOutput = """
            ## Tutorial
            Overview with nothing to apply.

            ## Files
            (nothing to change)
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal("Overview with nothing to apply.", result.Tutorial);
        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_SingleFileWithStep_ParsesPathContentAndStep()
    {
        const string cliOutput = """
            ## Tutorial
            Adds a Foo class.

            ## Files
            ### File: src/Foo.cs
            ```csharp
            public class Foo {}
            ```
            #### Tutorial
            ##### Step: Add Foo class
            This step adds the Foo class.
            ```csharp
            public class Foo {}
            ```
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal("Adds a Foo class.", result.Tutorial);
        var file = Assert.Single(result.Files);
        Assert.Equal("src/Foo.cs", file.FilePath);
        Assert.Equal("public class Foo {}", file.ProposedContent);

        var hunk = Assert.Single(file.Hunks);
        Assert.Equal("Add Foo class", hunk.Title);
        Assert.Equal("This step adds the Foo class.", hunk.Explanation);
        Assert.Equal("public class Foo {}", hunk.Snippet);
        Assert.Equal(new HunkLocation(0, 0), hunk.Location);
    }

    [Fact]
    public void Parse_FenceWithNoLanguageTag_StillParsesContent()
    {
        const string cliOutput = """
            ## Tutorial
            Overview.

            ## Files
            ### File: notes.txt
            ```
            plain text content
            ```
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        var file = Assert.Single(result.Files);
        Assert.Equal("notes.txt", file.FilePath);
        Assert.Equal("plain text content", file.ProposedContent);
    }

    [Fact]
    public void Parse_MultipleFiles_ParsesEachIndependently()
    {
        const string cliOutput = """
            ## Tutorial
            Adds two files.

            ## Files
            ### File: src/Foo.cs
            ```csharp
            public class Foo {}
            ```
            ### File: src/Bar.cs
            ```csharp
            public class Bar {}
            ```
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Equal(2, result.Files.Count);
        Assert.Equal("src/Foo.cs", result.Files[0].FilePath);
        Assert.Equal("public class Foo {}", result.Files[0].ProposedContent);
        Assert.Equal("src/Bar.cs", result.Files[1].FilePath);
        Assert.Equal("public class Bar {}", result.Files[1].ProposedContent);
    }

    [Fact]
    public void Parse_MultipleStepsForOneFile_ParsesEachStepInOrder()
    {
        const string cliOutput = """
            ## Tutorial
            Adds a Foo class in two steps.

            ## Files
            ### File: src/Foo.cs
            ```csharp
            public class Foo
            {
                public void Bar() {}
            }
            ```
            ##### Step: Declare the class
            First declare an empty class.
            ```csharp
            public class Foo {}
            ```
            ##### Step: Add a method
            Then add the Bar method.
            ```csharp
            public void Bar() {}
            ```
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        var file = Assert.Single(result.Files);
        Assert.Equal(2, file.Hunks.Count);
        Assert.Equal("Declare the class", file.Hunks[0].Title);
        Assert.Equal("Add a method", file.Hunks[1].Title);
    }

    [Fact]
    public void Parse_TruncatedFileFence_SkipsTheUnterminatedFile()
    {
        // The content fence is never closed, which is the "truncated model output" failure mode called
        // out in the roadmap: the file entry simply fails to match rather than being parsed as partial.
        const string cliOutput = """
            ## Tutorial
            Overview.

            ## Files
            ### File: src/Foo.cs
            ```csharp
            public class Foo {}
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        Assert.Empty(result.Files);
    }

    [Fact]
    public void Parse_FileContentContainingNestedFence_TruncatesAtFirstInnerClosingFence()
    {
        // Known fragility (see roadmap "Protocol fragility"): the non-greedy content match stops at the
        // first standalone ``` line, so a file whose own content documents a fenced code block gets cut
        // short instead of capturing the full intended content. This test pins today's actual behavior
        // so a future structured-output rework has a baseline to compare against.
        const string cliOutput = """
            ## Tutorial
            Overview.

            ## Files
            ### File: README.md
            ```markdown
            Example:
            ```
            some nested code
            ```
            ```
            """;

        var result = ClaudeResponseParser.Parse(cliOutput);

        var file = Assert.Single(result.Files);
        Assert.Equal("README.md", file.FilePath);
        Assert.Equal("Example:", file.ProposedContent);
    }
}
