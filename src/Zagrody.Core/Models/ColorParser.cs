using System.Globalization;

namespace Zagrody.Core.Models;

public static class ColorParser
{
    /// <summary>Accepts "#RRGGBB" or "#AARRGGBB".</summary>
    public static bool IsValidHex(string? value) => TryParse(value, out _, out _, out _, out _);

    public static bool TryParse(string? value, out byte a, out byte r, out byte g, out byte b)
    {
        a = 255;
        r = g = b = 0;
        if (string.IsNullOrEmpty(value) || value[0] != '#' || (value.Length != 7 && value.Length != 9))
        {
            return false;
        }
        if (!uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            return false;
        }
        if (value.Length == 7)
        {
            argb |= 0xFF000000;
        }
        a = (byte)(argb >> 24);
        r = (byte)(argb >> 16);
        g = (byte)(argb >> 8);
        b = (byte)argb;
        return true;
    }
}
