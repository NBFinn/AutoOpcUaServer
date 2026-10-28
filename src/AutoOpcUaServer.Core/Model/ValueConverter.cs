using System.Globalization;

namespace AutoOpcUaServer.Model;

/// <summary>Converts textual values from import files into CLR values of the matching type.</summary>
public static class ValueConverter
{
    public const char ArraySeparator = '|';

    public static Type ClrType(TagDataType type) => type switch
    {
        TagDataType.Boolean => typeof(bool),
        TagDataType.SByte => typeof(sbyte),
        TagDataType.Byte => typeof(byte),
        TagDataType.Int16 => typeof(short),
        TagDataType.UInt16 => typeof(ushort),
        TagDataType.Int32 => typeof(int),
        TagDataType.UInt32 => typeof(uint),
        TagDataType.Int64 => typeof(long),
        TagDataType.UInt64 => typeof(ulong),
        TagDataType.Float => typeof(float),
        TagDataType.Double => typeof(double),
        TagDataType.String => typeof(string),
        TagDataType.DateTime => typeof(DateTime),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>
    /// Converts <paramref name="text"/> to a scalar or a typed array (e.g. double[]).
    /// An empty text yields the default value of the type (or an empty array).
    /// </summary>
    public static object Convert(string? text, TagDataType type, bool isArray)
    {
        if (!isArray)
        {
            return ConvertScalar(text, type);
        }

        var parts = string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(ArraySeparator);

        var array = Array.CreateInstance(ClrType(type), parts.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            array.SetValue(ConvertScalar(parts[i], type), i);
        }

        return array;
    }

    public static object ConvertScalar(string? text, TagDataType type)
    {
        var s = text?.Trim() ?? "";
        if (s.Length == 0)
        {
            return DefaultValue(type);
        }

        var inv = CultureInfo.InvariantCulture;
        return type switch
        {
            TagDataType.Boolean => ParseBool(s),
            TagDataType.SByte => sbyte.Parse(s, NumberStyles.Integer, inv),
            TagDataType.Byte => byte.Parse(s, NumberStyles.Integer, inv),
            TagDataType.Int16 => short.Parse(s, NumberStyles.Integer, inv),
            TagDataType.UInt16 => ushort.Parse(s, NumberStyles.Integer, inv),
            TagDataType.Int32 => int.Parse(s, NumberStyles.Integer, inv),
            TagDataType.UInt32 => uint.Parse(s, NumberStyles.Integer, inv),
            TagDataType.Int64 => long.Parse(s, NumberStyles.Integer, inv),
            TagDataType.UInt64 => ulong.Parse(s, NumberStyles.Integer, inv),
            TagDataType.Float => float.Parse(NormalizeDecimal(s), NumberStyles.Float, inv),
            TagDataType.Double => double.Parse(NormalizeDecimal(s), NumberStyles.Float, inv),
            TagDataType.String => text ?? "",
            TagDataType.DateTime => DateTime.Parse(s, inv, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }

    public static object DefaultValue(TagDataType type) => type switch
    {
        TagDataType.String => "",
        TagDataType.DateTime => DateTime.UtcNow,
        _ => System.Convert.ChangeType(0, ClrType(type), CultureInfo.InvariantCulture),
    };

    /// <summary>Converts a simulated numeric value to the tag's type, rounding and clamping as needed.</summary>
    public static object FromDouble(double value, TagDataType type)
    {
        if (type == TagDataType.Boolean)
        {
            return value != 0;
        }

        if (TagDataTypes.IsInteger(type))
        {
            var (min, max) = IntegerRange(type);
            value = Math.Clamp(Math.Round(value), min, max);
        }

        return System.Convert.ChangeType(value, ClrType(type), CultureInfo.InvariantCulture);
    }

    public static (double Min, double Max) IntegerRange(TagDataType type) => type switch
    {
        TagDataType.SByte => (sbyte.MinValue, sbyte.MaxValue),
        TagDataType.Byte => (byte.MinValue, byte.MaxValue),
        TagDataType.Int16 => (short.MinValue, short.MaxValue),
        TagDataType.UInt16 => (ushort.MinValue, ushort.MaxValue),
        TagDataType.Int32 => (int.MinValue, int.MaxValue),
        TagDataType.UInt32 => (uint.MinValue, uint.MaxValue),
        TagDataType.Int64 => (long.MinValue, long.MaxValue),
        TagDataType.UInt64 => (ulong.MinValue, ulong.MaxValue),
        _ => (double.MinValue, double.MaxValue),
    };

    /// <summary>Parses a number written with either '.' or ',' as decimal separator.</summary>
    public static bool TryParseDouble(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return double.TryParse(NormalizeDecimal(text.Trim()), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool ParseBool(string s) => s.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" or "y" or "on" or "ja" or "wahr" => true,
        "false" or "0" or "no" or "n" or "off" or "nein" or "falsch" => false,
        _ => throw new FormatException($"'{s}' is not a valid boolean."),
    };

    // German exports often use ',' as decimal separator ("21,5"). Only rewrite it when there is no '.' as well.
    private static string NormalizeDecimal(string s) =>
        s.Contains(',') && !s.Contains('.') ? s.Replace(',', '.') : s;
}
