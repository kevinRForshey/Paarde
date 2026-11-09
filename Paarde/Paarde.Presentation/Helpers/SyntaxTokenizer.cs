using System.IO;

using TextMateSharp.Grammars;
using TextMateSharp.Registry;

namespace Paarde.Presentation.Helpers;

// T8.3.4: tokenizes a single line of source into syntax-highlighted runs using the same
// TextMateSharp grammars/theme CodeEditor uses for the full-file editor view, so a line shown in
// the Diffs page gets the same colors it would in the editor - just driven one line at a time
// (there's no single AvaloniaEdit document backing a diff row) rather than through
// AvaloniaEdit.TextMate's document-wide installation. specs/winui-frontend: moved here from
// Paarde.Avalonia so every front end's diff rows share one tokenizer - TextMateSharp is a plain .NET
// grammar engine, not a UI framework.
public static class SyntaxTokenizer
{
    private static readonly RegistryOptions RegistryOptions = new(ThemeName.DarkPlus);
    private static readonly Registry Registry = new(RegistryOptions);

    // Keyed by file extension (including the leading '.') - null is cached too, for an extension
    // with no known grammar, so a repeated miss doesn't re-ask the registry every time.
    private static readonly Dictionary<string, IGrammar?> GrammarByExtension = new();

    public static IReadOnlyList<SyntaxToken> Tokenize(string? filePath, string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
        {
            return Array.Empty<SyntaxToken>();
        }

        var grammar = GetGrammar(filePath);
        if (grammar is null)
        {
            return Array.Empty<SyntaxToken>();
        }

        var theme = Registry.GetTheme();
        var result = grammar.TokenizeLine(new LineText(lineText));
        var tokens = new List<SyntaxToken>(result.Tokens.Length);

        foreach (var token in result.Tokens)
        {
            var start = Math.Min(token.StartIndex, lineText.Length);
            var end = Math.Min(token.EndIndex, lineText.Length);

            if (end <= start)
            {
                continue;
            }

            var rules = theme.Match(token.Scopes);
            var color = rules.Count > 0 ? theme.GetColor(rules[0].foreground) : null;
            tokens.Add(new SyntaxToken(lineText[start..end], color));
        }

        return tokens;
    }

    private static IGrammar? GetGrammar(string? filePath)
    {
        var extension = string.IsNullOrEmpty(filePath) ? "" : Path.GetExtension(filePath);

        if (GrammarByExtension.TryGetValue(extension, out var cached))
        {
            return cached;
        }

        var language = string.IsNullOrEmpty(extension) ? null : RegistryOptions.GetLanguageByExtension(extension);
        var grammar = language is null ? null : Registry.LoadGrammar(RegistryOptions.GetScopeByLanguageId(language.Id));

        GrammarByExtension[extension] = grammar;
        return grammar;
    }
}
