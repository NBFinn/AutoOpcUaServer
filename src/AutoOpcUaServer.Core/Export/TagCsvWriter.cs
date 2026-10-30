using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Export;

/// <summary>Writes tags as a semicolon separated CSV file that the importer reads back unchanged.</summary>
public static class TagCsvWriter
{
    private static readonly (string Column, string Header)[] Columns =
    [
        ("path", "Path"),
        ("nodeid", "NodeId"),
        ("datatype", "DataType"),
        ("value", "Value"),
        ("access", "Access"),
        ("description", "Description"),
        ("simulation", "Simulation"),
        ("min", "Min"),
        ("max", "Max"),
        ("period", "Period"),
    ];

    public static void Write(IEnumerable<TagDefinition> tags, TextWriter writer)
    {
        writer.Write(string.Join(';', Columns.Select(c => c.Header)));
        writer.Write("\r\n");

        foreach (var tag in tags)
        {
            var row = TagRowMapper.ToRow(tag);
            writer.Write(string.Join(';', Columns.Select(c => Quote(row.GetValueOrDefault(c.Column)))));
            writer.Write("\r\n");
        }
    }

    public static async Task WriteFileAsync(IEnumerable<TagDefinition> tags, string path, CancellationToken ct = default)
    {
        using var writer = new StringWriter();
        Write(tags, writer);

        // UTF-8 with BOM so Excel shows umlauts correctly when the file is opened directly.
        await File.WriteAllTextAsync(path, writer.ToString(), new System.Text.UTF8Encoding(true), ct);
    }

    private static string Quote(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return "";
        }

        var needsQuotes = field.IndexOfAny([';', '"', '\r', '\n']) >= 0 || field.Trim().Length != field.Length;
        return needsQuotes ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }
}
