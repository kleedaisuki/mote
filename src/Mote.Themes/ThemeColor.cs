using System.Globalization;

namespace Mote.Themes;

/// <summary>An opaque sRGB color independent of any desktop UI framework.</summary>
public readonly record struct ThemeColor(byte Red, byte Green, byte Blue)
{
    /// <summary>Parses an opaque six-digit HTML color, for example <c>#1E1E1E</c>.</summary>
    public static ThemeColor FromHex(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length != 7 || hex[0] != '#' ||
            !byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            throw new FormatException("Theme colors must use opaque #RRGGBB syntax.");
        }
        return new ThemeColor(red, green, blue);
    }

    /// <summary>Returns an uppercase <c>#RRGGBB</c> string for UI adapters.</summary>
    public string ToHex() => $"#{Red:X2}{Green:X2}{Blue:X2}";

    /// <summary>Computes the WCAG contrast ratio between two opaque sRGB colors.</summary>
    public double ContrastRatio(ThemeColor other)
    {
        var first = RelativeLuminance();
        var second = other.RelativeLuminance();
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private double RelativeLuminance() =>
        0.2126 * Linear(Red) + 0.7152 * Linear(Green) + 0.0722 * Linear(Blue);

    private static double Linear(byte channel)
    {
        var value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
