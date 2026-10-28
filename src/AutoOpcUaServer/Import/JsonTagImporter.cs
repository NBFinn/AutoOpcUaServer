using System.Globalization;
using System.Text.Json;

namespace AutoOpcUaServer.Import;

/// <summary>
/// Reads tags from a JSON file. Accepted shapes:
/// <code>
/// [ { "path": "Plant/Temp", "dataType": "Double", "value": 21.5 }, ... ]
/// { "tags": [ ... ] }
/// </code>
/// Property names are the same as the CSV column names (case-insensitive). Array values may be
/// given as JSON arrays.
/// </summary>
public sealed class JsonTagImporter : ITagImporter
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public IReadOnlyCollection<string> Extensions { get; } = [".json"];

    public void Import(string filePath, TagRowMapper mapper, ImportResult result) =>
        ImportText(File.ReadAllText(filePath), Path.GetFileName(filePath), mapper, result);

    public void ImportText(string json, string sourceName, TagRowMapper mapper, ImportResult result)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"{sourceName}: invalid JSON ({ex.Message}).");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            JsonElement tags;
            if (root.ValueKind == JsonValueKind.Array)
            {
                tags = root;
            }
            else if (root.ValueKind == JsonValueKind.Object && TryGetProperty(root, "tags", out tags) && tags.ValueKind == JsonValueKind.Array)
            {
            }
            else
            {
                result.Errors.Add($"{sourceName}: expected an array of tags or an object with a \"tags\" array.");
                return;
            }

            var index = 0;
            foreach (var item in tags.EnumerateArray())
            {
                var source = $"{sourceName}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    result.Errors.Add($"{source}: expected an object.");
                    continue;
                }

                var row = new Dictionary<string, string?>();
                foreach (var property in item.EnumerateObject())
                {
                    var column = TagRowMapper.CanonicalColumn(property.Name);
                    if (column is null)
                    {
                        result.Warnings.Add($"{source}: unknown property '{property.Name}' is ignored.");
                        continue;
                    }

                    row[column] = ToText(property.Value);
                }

                var tag = mapper.Map(row, source, result.Errors);
                if (tag is not null)
                {
                    result.Tags.Add(tag);
                }
            }
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => string.Join(Model.ValueConverter.ArraySeparator, value.EnumerateArray().Select(ToText)),
        _ => value.GetRawText(),
    };
}
