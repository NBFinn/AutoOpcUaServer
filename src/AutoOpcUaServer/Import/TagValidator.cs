using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Import;

/// <summary>Checks the combined set of tags for conflicts before the address space is built.</summary>
public static class TagValidator
{
    public static void Validate(IReadOnlyList<TagDefinition> tags, char pathSeparator, ICollection<string> errors, ICollection<string> warnings)
    {
        var variables = new Dictionary<string, TagDefinition>(StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            var segments = TagPaths.Split(tag.Path, pathSeparator);
            if (segments.Length == 0 || segments.Any(s => s.Length == 0))
            {
                errors.Add($"{tag.Source}: invalid path '{tag.Path}' (empty folder or name).");
                continue;
            }

            var key = string.Join(pathSeparator, segments);
            if (variables.TryGetValue(key, out var existing))
            {
                errors.Add($"{tag.Source}: '{key}' is already defined in {existing.Source}.");
                continue;
            }

            variables[key] = tag;
            for (var i = 1; i < segments.Length; i++)
            {
                folders.Add(string.Join(pathSeparator, segments[..i]));
            }

            ValidateValue(tag, errors);
            ValidateSimulation(tag, errors);
        }

        foreach (var (key, tag) in variables)
        {
            if (folders.Contains(key))
            {
                errors.Add($"{tag.Source}: '{key}' is used both as a variable and as a folder.");
            }
        }

        // Folder NodeIds are derived from their path, variable NodeIds may be set explicitly.
        foreach (var folder in folders)
        {
            nodeIds[TagPaths.FolderNodeId(folder, pathSeparator)] = $"folder '{folder}'";
        }

        foreach (var tag in variables.Values)
        {
            var nodeId = TagPaths.VariableNodeId(tag, pathSeparator);
            if (!nodeIds.TryAdd(nodeId, $"{tag.Source}"))
            {
                errors.Add($"{tag.Source}: NodeId 's={nodeId}' is already used by {nodeIds[nodeId]}.");
            }
        }

        if (tags.Count == 0)
        {
            warnings.Add("No tags were imported; the server will only expose its standard nodes.");
        }
    }

    private static void ValidateValue(TagDefinition tag, ICollection<string> errors)
    {
        try
        {
            ValueConverter.Convert(tag.Value, tag.DataType, tag.IsArray);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            errors.Add($"{tag.Source} ({tag.Path}): value '{tag.Value}' is not a valid {tag.DataType}{(tag.IsArray ? "[]" : "")}.");
        }
    }

    private static void ValidateSimulation(TagDefinition tag, ICollection<string> errors)
    {
        if (tag.Min is { } min && tag.Max is { } max && min > max)
        {
            errors.Add($"{tag.Source} ({tag.Path}): Min ({min}) is greater than Max ({max}).");
        }

        if (tag.Period is <= 0)
        {
            errors.Add($"{tag.Source} ({tag.Path}): Period must be greater than 0.");
        }

        if (tag.Simulation == SimulationKind.None)
        {
            return;
        }

        var prefix = $"{tag.Source} ({tag.Path}): simulation '{tag.Simulation}'";
        if (tag.IsArray)
        {
            errors.Add($"{prefix} is not supported for arrays.");
        }
        else if (tag.DataType is TagDataType.String or TagDataType.DateTime)
        {
            errors.Add($"{prefix} is not supported for {tag.DataType}.");
        }
        else if (tag.DataType == TagDataType.Boolean && tag.Simulation is not (SimulationKind.Random or SimulationKind.Toggle))
        {
            errors.Add($"{prefix} is not supported for Boolean (use Random or Toggle).");
        }
    }
}

/// <summary>Path handling shared by the validator and the node manager.</summary>
public static class TagPaths
{
    public static string[] Split(string path, char separator) =>
        path.Trim().Trim(separator).Split(separator).Select(s => s.Trim()).ToArray();

    /// <summary>Folders get the string NodeId "Seg1.Seg2".</summary>
    public static string FolderNodeId(string folderPath, char separator) =>
        string.Join('.', Split(folderPath, separator));

    /// <summary>Variables use their explicit NodeId, otherwise "Folder.SubFolder.Name".</summary>
    public static string VariableNodeId(TagDefinition tag, char separator) =>
        string.IsNullOrWhiteSpace(tag.NodeId)
            ? string.Join('.', Split(tag.Path, separator))
            : StripPrefix(tag.NodeId.Trim());

    // Allow users to write "s=MyTag" or "ns=2;s=MyTag" – the namespace is always the server's tag namespace.
    private static string StripPrefix(string nodeId)
    {
        var s = nodeId;
        if (s.StartsWith("ns=", StringComparison.OrdinalIgnoreCase) && s.IndexOf(';') is > 0 and var semicolon)
        {
            s = s[(semicolon + 1)..];
        }

        return s.StartsWith("s=", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
    }
}
