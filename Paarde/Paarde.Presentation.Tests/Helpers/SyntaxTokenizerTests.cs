using Paarde.Presentation.Helpers;

namespace Paarde.Presentation.Tests.Helpers;

public class SyntaxTokenizerTests
{
    [Fact]
    public void Tokenize_KnownExtension_ColorsAKeywordToken()
    {
        var tokens = SyntaxTokenizer.Tokenize("Program.cs", "public static void Main()");

        Assert.Contains(tokens, t => t.Text == "public" && t.Color is not null);
    }

    [Fact]
    public void Tokenize_KnownExtension_TokensReconstructTheLineExactly()
    {
        const string line = "public static void Main()";

        var tokens = SyntaxTokenizer.Tokenize("Program.cs", line);

        Assert.Equal(line, string.Concat(tokens.Select(t => t.Text)));
    }

    [Fact]
    public void Tokenize_UnknownExtension_ReturnsNoTokens()
    {
        var tokens = SyntaxTokenizer.Tokenize("notes.somethingmadeup", "public static void Main()");

        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_NullFilePath_ReturnsNoTokens()
    {
        var tokens = SyntaxTokenizer.Tokenize(null, "public static void Main()");

        Assert.Empty(tokens);
    }

    [Fact]
    public void Tokenize_EmptyLine_ReturnsNoTokens()
    {
        var tokens = SyntaxTokenizer.Tokenize("Program.cs", "");

        Assert.Empty(tokens);
    }
}
