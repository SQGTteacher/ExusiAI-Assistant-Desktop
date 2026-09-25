using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace ExusiAI.Plugin.MishaShowcase;

/// <summary>
/// Converts ClassIsland/Avalonia color values without letting WPF reinterpret 8-digit
/// strings as AARRGGBB. ClassIsland persists them as RRGGBBAA (for example #00BFFFFF).
/// </summary>
internal static class ClassIslandColorCodec
{
    public static Color Parse(JsonNode? node, Color fallback)
    {
        if (node is JsonValue value && value.TryGetValue<string>(out var text) &&
            TryParse(text, out var parsed))
            return parsed;

        if (node is JsonObject obj)
        {
            var a = ReadByte(obj, "A", 255);
            var r = ReadByte(obj, "R", 0);
            var g = ReadByte(obj, "G", 0);
            var b = ReadByte(obj, "B", 0);
            return Color.FromArgb(a, r, g, b);
        }

        return fallback;
    }

    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 8 &&
                TryByte(hex.AsSpan(0, 2), out var r) &&
                TryByte(hex.AsSpan(2, 2), out var g) &&
                TryByte(hex.AsSpan(4, 2), out var b) &&
                TryByte(hex.AsSpan(6, 2), out var a))
            {
                color = Color.FromArgb(a, r, g, b);
                return true;
            }

            if (hex.Length == 6 &&
                TryByte(hex.AsSpan(0, 2), out r) &&
                TryByte(hex.AsSpan(2, 2), out g) &&
                TryByte(hex.AsSpan(4, 2), out b))
            {
                color = Color.FromArgb(255, r, g, b);
                return true;
            }

            if (hex.Length == 4 &&
                TryNibble(hex[0], out var rn) &&
                TryNibble(hex[1], out var gn) &&
                TryNibble(hex[2], out var bn) &&
                TryNibble(hex[3], out var an))
            {
                color = Color.FromArgb(Expand(an), Expand(rn), Expand(gn), Expand(bn));
                return true;
            }

            if (hex.Length == 3 &&
                TryNibble(hex[0], out rn) &&
                TryNibble(hex[1], out gn) &&
                TryNibble(hex[2], out bn))
            {
                color = Color.FromArgb(255, Expand(rn), Expand(gn), Expand(bn));
                return true;
            }
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(value);
            if (converted is Color named)
            {
                color = named;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        return false;
    }

    public static string Format(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private static bool TryByte(ReadOnlySpan<char> value, out byte result) =>
        byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

    private static bool TryNibble(char value, out byte result)
    {
        if (value is >= '0' and <= '9')
        {
            result = (byte)(value - '0');
            return true;
        }
        if (value is >= 'a' and <= 'f')
        {
            result = (byte)(value - 'a' + 10);
            return true;
        }
        if (value is >= 'A' and <= 'F')
        {
            result = (byte)(value - 'A' + 10);
            return true;
        }
        result = 0;
        return false;
    }

    private static byte Expand(byte nibble) => (byte)((nibble << 4) | nibble);

    private static byte ReadByte(JsonObject node, string key, byte fallback)
    {
        if (node[key] is JsonValue value)
        {
            if (value.TryGetValue<byte>(out var small)) return small;
            if (value.TryGetValue<int>(out var number)) return (byte)Math.Clamp(number, 0, 255);
        }
        return fallback;
    }
}
