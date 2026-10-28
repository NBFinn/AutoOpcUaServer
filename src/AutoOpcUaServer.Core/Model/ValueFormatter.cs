using System.Globalization;

namespace AutoOpcUaServer.Model;

/// <summary>Formats values for display, using the same notation as the import files.</summary>
public static class ValueFormatter
{
    public static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        double d => d.ToString("0.###", CultureInfo.InvariantCulture),
        float f => f.ToString("0.###", CultureInfo.InvariantCulture),
        Array array => string.Join(ValueConverter.ArraySeparator, array.Cast<object?>().Select(FormatExact)),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    // Array elements are formatted without rounding so the text can be edited and set again.
    private static string FormatExact(object? value) => value switch
    {
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        _ => Format(value),
    };
}
