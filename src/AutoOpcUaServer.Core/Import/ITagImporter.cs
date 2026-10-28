using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Import;

/// <summary>Reads tag definitions from one file format.</summary>
public interface ITagImporter
{
    /// <summary>File extensions handled by this importer, including the dot (".csv").</summary>
    IReadOnlyCollection<string> Extensions { get; }

    void Import(string filePath, TagRowMapper mapper, ImportResult result);
}

/// <summary>Collected output of one or more imports.</summary>
public sealed class ImportResult
{
    public List<TagDefinition> Tags { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Files { get; } = [];
}
