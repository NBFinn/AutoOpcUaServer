using System.Globalization;
using ClosedXML.Excel;

namespace AutoOpcUaServer.Import;

/// <summary>
/// Reads tags from an Excel workbook (.xlsx/.xlsm). Every worksheet whose first row contains a
/// "Path" or "Name" column is imported; the columns are the same as for CSV files.
/// </summary>
public sealed class ExcelTagImporter : ITagImporter
{
    public IReadOnlyCollection<string> Extensions { get; } = [".xlsx", ".xlsm"];

    public void Import(string filePath, TagRowMapper mapper, ImportResult result)
    {
        var fileName = Path.GetFileName(filePath);

        // Open with FileShare.ReadWrite so the file can stay open in Excel while the server reads it.
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);

        var importedSheets = 0;
        foreach (var sheet in workbook.Worksheets)
        {
            var used = sheet.RangeUsed();
            if (used is null)
            {
                continue;
            }

            var headerRow = used.FirstRow();
            var columns = headerRow.Cells()
                .Select(c => (Column: c.Address.ColumnNumber, Name: TagRowMapper.CanonicalColumn(c.GetString())))
                .Where(c => c.Name is not null)
                .ToList();

            if (!columns.Any(c => c.Name is "path" or "name"))
            {
                result.Warnings.Add($"{fileName}[{sheet.Name}]: no 'Path' or 'Name' column in the first row, sheet skipped.");
                continue;
            }

            importedSheets++;
            foreach (var row in used.RowsUsed().Skip(1))
            {
                var values = new Dictionary<string, string?>();
                foreach (var (column, name) in columns)
                {
                    values[name!] = CellText(sheet.Cell(row.RowNumber(), column));
                }

                if (values.Values.All(string.IsNullOrWhiteSpace) || values.GetValueOrDefault("path")?.TrimStart().StartsWith('#') == true)
                {
                    continue;
                }

                var tag = mapper.Map(values, $"{fileName}[{sheet.Name}]:{row.RowNumber()}", result.Errors);
                if (tag is not null)
                {
                    result.Tags.Add(tag);
                }
            }
        }

        if (importedSheets == 0)
        {
            result.Errors.Add($"{fileName}: no worksheet with a 'Path' or 'Name' column found.");
        }
    }

    private static string? CellText(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsBlank)
        {
            return null;
        }

        if (value.IsNumber)
        {
            return value.GetNumber().ToString("R", CultureInfo.InvariantCulture);
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean() ? "true" : "false";
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime().ToString("o", CultureInfo.InvariantCulture);
        }

        return cell.GetString();
    }
}
