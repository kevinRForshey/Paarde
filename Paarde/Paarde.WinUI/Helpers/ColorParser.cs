using System.Globalization;

using Windows.UI;

namespace Paarde.WinUI.Helpers;

// WinUI has no Color.TryParse. Parses the "#RGB"/"#RRGGBB"/"#RRGGBBAA" strings TextMate themes use
// (SyntaxToken.Color) - note TextMate's alpha comes last, unlike XAML's "#AARRGGBB".
public static class ColorParser
{
    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(value) || value[0] != '#')
        {
            return false;
        }

        var hex = value[1..];
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if ((hex.Length != 6 && hex.Length != 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed))
        {
            return false;
        }

        if (hex.Length == 6)
        {
            packed = (packed << 8) | 0xFF;
        }

        color = Color.FromArgb((byte)packed, (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8));
        return true;
    }
}
