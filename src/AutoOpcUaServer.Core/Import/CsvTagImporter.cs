using System.Text;

namespace AutoOpcUaServer.Import;

/// <summary>
/// Reads tags from a CSV file with a header row. The delimiter (';', ',' or tab) is detected
/// from the header. Fields may be quoted with '"'. Lines starting with '#' are ignored.
/// </summary>
public sealed class CsvTagImporter : ITagImporter
{
    private static readonly char[] DelimiterCandidates = [';', '\t', ','];

    public IReadOnlyCollection<string> Extensions { get; } = [".csv", ".tsv", ".txt"];

    public void Import(string filePath, TagRowMapper mapper, ImportResult result)
    {
        var text = File.ReadAllText(filePath, Encoding.UTF8);
        ImportText(text, Path.GetFileName(filePath), mapper, result);
    }

    public void ImportText(string text, string sourceName, TagRowMapper mapper, ImportResult result)
    {
        var records = Parse(text).Where(r => !IsBlankOrComment(r.Fields)).ToList();
        if (records.Count == 0)
        {
            result.Errors.Add($"{sourceName}: file is empty or has no header row.");
            return;
        }

        var header = records[0];
        var columns = header.Fields.Select(h => TagRowMapper.CanonicalColumn(h) ?? "").ToArray();
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].Length == 0 && header.Fields[i].Trim().Length > 0)
            {
                result.Warnings.Add($"{sourceName}:{header.Line}: unknown column '{header.Fields[i].Trim()}' is ignored.");
            }
        }

        foreach (var (fields, line) in records.Skip(1))
        {
            var row = new Dictionary<string, string?>();
            for (var i = 0; i < columns.Length && i < fields.Count; i++)
            {
                if (columns[i].Length > 0)
                {
                    row[columns[i]] = fields[i];
                }
            }

            var tag = mapper.Map(row, $"{sourceName}:{line}", result.Errors);
            if (tag is not null)
            {
                result.Tags.Add(tag);
            }
        }
    }

    /// <summary>Picks the candidate delimiter that occurs most often in the first content line.</summary>
    internal static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0 && !l.StartsWith('#')) ?? "";
        return DelimiterCandidates.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();
    }

    /// <summary>
    /// RFC 4180 style parser: quoted fields may contain delimiters, doubled quotes ("") and line breaks.
    /// Returns every record together with the line number it starts on.
    /// </summary>
    internal static IEnumerable<(List<string> Fields, int Line)> Parse(string text)
    {
        var delimiter = DetectDelimiter(text);
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    if (c != '\r')
                    {
                        field.Append(c);
                    }
                }
            }
            else if (c == '"' && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                fields.Add(field.ToString());
                yield return (fields, recordLine);
                fields = [];
                field.Clear();
                line++;
                recordLine = line;
            }
            else if (c != '\r')
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return (fields, recordLine);
        }
    }

    private static bool IsBlankOrComment(List<string> fields) =>
        fields.All(string.IsNullOrWhiteSpace) || fields[0].TrimStart().StartsWith('#');
}
