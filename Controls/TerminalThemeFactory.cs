using System.Windows.Media;
using Microsoft.Terminal.Wpf;

namespace StorageTerm.Controls;

public static class TerminalThemeFactory
{
    private static readonly Lazy<IReadOnlyList<string>> InstalledFonts = new(() =>
        Fonts.SystemFontFamilies
             .Select(f => f.Source)
             .Where(s => !string.IsNullOrWhiteSpace(s))
             .Distinct(StringComparer.OrdinalIgnoreCase)
             .ToArray());

    public static string ResolveFontFamily(string preferred)
    {
        var fonts = InstalledFonts.Value;

        if (fonts.Any(f => f.Equals(preferred, StringComparison.OrdinalIgnoreCase)))
        {
            return preferred;
        }

        foreach (var candidate in new[] { "Cascadia Mono", "Cascadia Code", "Consolas", "Courier New" })
        {
            if (fonts.Any(f => f.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return "Consolas";
    }

    public static TerminalTheme Create() => new()
    {
        DefaultBackground = Ref(0x0A, 0x0A, 0x0A),
        DefaultForeground = Ref(0xE5, 0xE5, 0xE5),
        DefaultSelectionBackground = Ref(0x2A, 0x2A, 0x2A),
        CursorStyle = CursorStyle.BlinkingUnderline,
        ColorTable = new[]
        {
            Ref(0x10, 0x12, 0x14), // black
            Ref(0xFF, 0x7A, 0x85), // red
            Ref(0x7E, 0xE7, 0x87), // green
            Ref(0xE3, 0xB3, 0x41), // yellow
            Ref(0x79, 0xC0, 0xFF), // blue
            Ref(0xD2, 0xA8, 0xFF), // magenta
            Ref(0x56, 0xD4, 0xDD), // cyan
            Ref(0xC9, 0xD1, 0xD9), // white
            Ref(0x6E, 0x76, 0x81), // bright black
            Ref(0xFF, 0xA1, 0x98), // bright red
            Ref(0xA5, 0xF3, 0xB0), // bright green
            Ref(0xF0, 0xC6, 0x74), // bright yellow
            Ref(0xA5, 0xD6, 0xFF), // bright blue
            Ref(0xE2, 0xC5, 0xFF), // bright magenta
            Ref(0x9B, 0xEF, 0xF5), // bright cyan
            Ref(0xFF, 0xFF, 0xFF)  // bright white
        }
    };

    /// <summary>Win32 COLORREF: 0x00BBGGRR.</summary>
    private static uint Ref(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));
}

