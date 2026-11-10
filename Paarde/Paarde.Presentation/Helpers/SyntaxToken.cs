namespace Paarde.Presentation.Helpers;

// One run of a tokenized source line, with the foreground color (a "#RRGGBB"/"#AARRGGBB"
// string, as TextMateSharp's Theme.GetColor returns it) the current syntax theme assigns it - null
// when the theme has no specific color for that token's scopes, meaning "leave the ambient
// foreground alone". See SyntaxTokenizer.
public record SyntaxToken(string Text, string? Color);
