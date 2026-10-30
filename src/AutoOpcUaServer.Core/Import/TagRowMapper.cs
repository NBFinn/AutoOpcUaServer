using System.Globalization;
using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Import;

/// <summary>
/// Turns one table row (column name → text) into a <see cref="TagDefinition"/>.
/// Shared by the CSV, Excel and JSON importers so all formats accept the same columns.
/// </summary>
public sealed class TagRowMapper
{
    private static readonly Dictionary<string, string> ColumnAliases = BuildAliases(new()
    {
        ["path"] = ["path", "pfad", "tag", "tagname", "variable", "browsepath"],
        ["folder"] = ["folder", "ordner", "group", "gruppe"],
        ["name"] = ["name", "displayname"],
        ["nodeid"] = ["nodeid", "id"],
        ["datatype"] = ["datatype", "type", "datentyp", "typ"],
        ["value"] = ["value", "initialvalue", "wert", "startwert", "default"],
        ["access"] = ["access", "accesslevel", "zugriff"],
        ["description"] = ["description", "beschreibung", "comment", "kommentar"],
        ["simulation"] = ["simulation", "sim"],
        ["min"] = ["min", "minimum"],
        ["max"] = ["max", "maximum"],
        ["period"] = ["period", "periode", "periodsec", "periods"],
    });

    private readonly char _pathSeparator;

    public TagRowMapper(char pathSeparator = '/')
    {
        _pathSeparator = pathSeparator;
    }

    /// <summary>Normalizes a column header to its canonical name, or returns null for unknown columns.</summary>
    public static string? CanonicalColumn(string header)
    {
        var key = new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return ColumnAliases.TryGetValue(key, out var canonical) ? canonical : null;
    }

    /// <summary>
    /// Inverse of <see cref="Map"/>: the row (canonical column → text) that describes <paramref name="tag"/>.
    /// Empty columns are left out.
    /// </summary>
    public static Dictionary<string, string?> ToRow(TagDefinition tag)
    {
        static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

        var row = new Dictionary<string, string?>
        {
            ["path"] = tag.Path,
            ["nodeid"] = tag.NodeId,
            ["datatype"] = tag.DataType + (tag.IsArray ? "[]" : ""),
            ["value"] = tag.Value,
            ["access"] = tag.Access == TagAccess.ReadWrite ? "RW" : "R",
            ["description"] = tag.Description,
            ["simulation"] = tag.Simulation == SimulationKind.None ? null : tag.Simulation.ToString(),
            ["min"] = Number(tag.Min),
            ["max"] = Number(tag.Max),
            ["period"] = Number(tag.Period),
        };

        foreach (var key in row.Where(r => string.IsNullOrEmpty(r.Value)).Select(r => r.Key).ToList())
        {
            row.Remove(key);
        }

        return row;
    }

    /// <summary>
    /// Maps a row whose keys are canonical column names. Returns null and adds to
    /// <paramref name="errors"/> if the row is invalid.
    /// </summary>
    public TagDefinition? Map(IReadOnlyDictionary<string, string?> row, string source, ICollection<string> errors)
    {
        string? Get(string column) =>
            row.TryGetValue(column, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var rowErrors = new List<string>();

        var path = Get("path");
        var folder = Get("folder");
        var name = Get("name");
        if (path is null && name is not null)
        {
            path = folder is null ? name : folder.TrimEnd(_pathSeparator) + _pathSeparator + name;
        }

        if (path is null)
        {
            errors.Add($"{source}: missing 'Path' (or 'Name').");
            return null;
        }

        var dataType = TagDataType.Double;
        var isArray = false;
        var typeText = Get("datatype");
        if (typeText is not null && !TagDataTypes.TryParse(typeText, out dataType, out isArray))
        {
            rowErrors.Add($"unknown data type '{typeText}'");
        }

        var access = TagAccess.ReadWrite;
        var accessText = Get("access");
        if (accessText is not null && !TryParseAccess(accessText, out access))
        {
            rowErrors.Add($"unknown access '{accessText}' (use R or RW)");
        }

        var simulation = SimulationKind.None;
        var simText = Get("simulation");
        if (simText is not null && !TryParseSimulation(simText, out simulation))
        {
            rowErrors.Add($"unknown simulation '{simText}' (use {string.Join(", ", Enum.GetNames<SimulationKind>())})");
        }

        double? ParseNumber(string column)
        {
            var text = Get(column);
            if (text is null)
            {
                return null;
            }

            if (ValueConverter.TryParseDouble(text, out var number))
            {
                return number;
            }

            rowErrors.Add($"'{column}' value '{text}' is not a number");
            return null;
        }

        var min = ParseNumber("min");
        var max = ParseNumber("max");
        var period = ParseNumber("period");

        if (rowErrors.Count > 0)
        {
            errors.Add($"{source} ({path}): {string.Join("; ", rowErrors)}.");
            return null;
        }

        return new TagDefinition
        {
            Path = path,
            NodeId = Get("nodeid"),
            DataType = dataType,
            IsArray = isArray,
            // Keep string values untrimmed so leading/trailing blanks survive.
            Value = row.TryGetValue("value", out var rawValue) ? rawValue : null,
            Access = access,
            Description = Get("description"),
            Simulation = simulation,
            Min = min,
            Max = max,
            Period = period,
            Source = source,
        };
    }

    private static bool TryParseAccess(string text, out TagAccess access)
    {
        switch (text.Trim().ToLowerInvariant().Replace(" ", ""))
        {
            case "r":
            case "ro":
            case "read":
            case "readonly":
            case "lesen":
                access = TagAccess.Read;
                return true;
            case "rw":
            case "w":
            case "readwrite":
            case "read/write":
            case "write":
            case "schreiben":
            case "lesen/schreiben":
                access = TagAccess.ReadWrite;
                return true;
            default:
                access = TagAccess.ReadWrite;
                return false;
        }
    }

    private static bool TryParseSimulation(string text, out SimulationKind kind)
    {
        var s = text.Trim();
        if (s.Equals("none", StringComparison.OrdinalIgnoreCase) || s == "-" || s.Equals("keine", StringComparison.OrdinalIgnoreCase))
        {
            kind = SimulationKind.None;
            return true;
        }

        return Enum.TryParse(s, ignoreCase: true, out kind) && Enum.IsDefined(kind) && !int.TryParse(s, out _);
    }

    private static Dictionary<string, string> BuildAliases(Dictionary<string, string[]> map)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, aliases) in map)
        {
            foreach (var alias in aliases)
            {
                result[alias] = canonical;
            }
        }

        return result;
    }
}
