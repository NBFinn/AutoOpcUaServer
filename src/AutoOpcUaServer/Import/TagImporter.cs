namespace AutoOpcUaServer.Import;

/// <summary>
/// Entry point for importing: picks the right importer per file extension, expands directories
/// and validates the combined result.
/// </summary>
public sealed class TagImporter
{
    private readonly ITagImporter[] _importers =
    [
        new CsvTagImporter(),
        new ExcelTagImporter(),
        new JsonTagImporter(),
    ];

    private readonly char _pathSeparator;

    public TagImporter(char pathSeparator = '/')
    {
        _pathSeparator = pathSeparator;
    }

    public IEnumerable<string> SupportedExtensions => _importers.SelectMany(i => i.Extensions);

    /// <summary>Imports all given files and directories (non-recursive) and validates the tags.</summary>
    public ImportResult Import(IEnumerable<string> paths)
    {
        var result = new ImportResult();
        var mapper = new TagRowMapper(_pathSeparator);

        foreach (var file in ExpandPaths(paths, result))
        {
            var importer = FindImporter(file);
            if (importer is null)
            {
                result.Errors.Add($"{file}: unsupported file type (supported: {string.Join(", ", SupportedExtensions)}).");
                continue;
            }

            result.Files.Add(file);
            try
            {
                importer.Import(file, mapper, result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException)
            {
                result.Errors.Add($"{Path.GetFileName(file)}: could not be read ({ex.Message}).");
            }
        }

        if (result.Errors.Count == 0)
        {
            TagValidator.Validate(result.Tags, _pathSeparator, result.Errors, result.Warnings);
        }

        return result;
    }

    private ITagImporter? FindImporter(string file)
    {
        var extension = Path.GetExtension(file);
        return _importers.FirstOrDefault(i => i.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    private IEnumerable<string> ExpandPaths(IEnumerable<string> paths, ImportResult result)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var files = Directory.EnumerateFiles(path)
                    .Where(f => FindImporter(f) is not null && !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (files.Count == 0)
                {
                    result.Warnings.Add($"{path}: directory contains no importable files.");
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
            else if (File.Exists(path))
            {
                yield return path;
            }
            else
            {
                result.Errors.Add($"{path}: file or directory not found.");
            }
        }
    }
}
