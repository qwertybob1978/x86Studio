using System.Globalization;

namespace ClassicMacIde.Core;

public readonly record struct ClassicMacRgbColor(byte Red, byte Green, byte Blue)
{
    public static bool TryParse(string? value, out ClassicMacRgbColor color)
    {
        color = default;
        if (value is null ||
            value.Length != 7 ||
            value[0] != '#' ||
            !uint.TryParse(
                value.AsSpan(1),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out var rgb))
        {
            return false;
        }

        color = new ClassicMacRgbColor(
            (byte)(rgb >> 16),
            (byte)(rgb >> 8),
            (byte)rgb);
        return true;
    }

    public static ClassicMacRgbColor Parse(string? value) =>
        TryParse(value, out var color)
            ? color
            : throw new FormatException("Color must use the #RRGGBB format.");

    public ushort Red16 => ToQuickDrawChannel(Red);
    public ushort Green16 => ToQuickDrawChannel(Green);
    public ushort Blue16 => ToQuickDrawChannel(Blue);

    private static ushort ToQuickDrawChannel(byte value) =>
        (ushort)(value * 257);
}
