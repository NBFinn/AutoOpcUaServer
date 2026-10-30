using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using ClosedXML.Excel;

namespace AutoOpcUaServer.Tests;

public sealed class ImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("autoopcua-import-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static ImportResult Import(params string[] paths) => new TagImporter().Import(paths);

    [Fact]
    public void Csv_with_semicolons_and_german_headers_is_imported()
    {
        var file = WriteFile("tags.csv", """
            # comment
            Pfad;Datentyp;Wert;Zugriff;Beschreibung;Simulation;Min;Max
            Plant/Line1/Temp;Double;21,5;R;Temperatur;Sine;10;30
            Plant/Line1/On;bool;ja;RW;;;;
            """);

        var result = Import(file);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tags.Count);
        var temp = result.Tags[0];
        Assert.Equal("Plant/Line1/Temp", temp.Path);
        Assert.Equal(TagDataType.Double, temp.DataType);
        Assert.Equal(TagAccess.Read, temp.Access);
        Assert.Equal(SimulationKind.Sine, temp.Simulation);
        Assert.Equal(10, temp.Min);
        Assert.Equal(30, temp.Max);
        Assert.Equal(21.5, ValueConverter.Convert(temp.Value, temp.DataType, false));
        Assert.Equal(true, ValueConverter.Convert(result.Tags[1].Value, TagDataType.Boolean, false));
    }

    [Fact]
    public void Csv_with_commas_quotes_and_arrays_is_imported()
    {
        var file = WriteFile("tags.csv",
            "Folder,Name,DataType,Value,Description\r\n" +
            "A,Text,String,\"Hello, \"\"World\"\"\",\"multi\nline\"\r\n" +
            "A,Values,Int32[],1|2|3,\r\n");

        var result = Import(file);

        Assert.Empty(result.Errors);
        Assert.Equal("A/Text", result.Tags[0].Path);
        Assert.Equal("Hello, \"World\"", result.Tags[0].Value);
        Assert.Equal("multi\nline", result.Tags[0].Description);
        Assert.Equal(new[] { 1, 2, 3 }, ValueConverter.Convert(result.Tags[1].Value, TagDataType.Int32, true));
        Assert.Contains(":4", result.Tags[1].Source);
    }

    [Fact]
    public void Json_with_arrays_and_nodeid_is_imported()
    {
        var file = WriteFile("tags.json", """
            { "tags": [
              { "path": "Robot/Offsets", "dataType": "Float[]", "value": [0.5, -1] },
              { "Path": "Robot/State", "NodeId": "ns=2;s=Robot.State", "DataType": "String", "Value": "Idle" },
            ] }
            """);

        var result = Import(file);

        Assert.Empty(result.Errors);
        Assert.Equal(new[] { 0.5f, -1f }, ValueConverter.Convert(result.Tags[0].Value, TagDataType.Float, true));
        Assert.Equal("Robot.State", TagPaths.VariableNodeId(result.Tags[1], '/'));
    }

    [Fact]
    public void Excel_sheets_are_imported()
    {
        var path = Path.Combine(_dir, "tags.xlsx");
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Tags");
            sheet.Cell(1, 1).Value = "Path";
            sheet.Cell(1, 2).Value = "DataType";
            sheet.Cell(1, 3).Value = "Value";
            sheet.Cell(2, 1).Value = "Tank/Level";
            sheet.Cell(2, 2).Value = "Double";
            sheet.Cell(2, 3).Value = 12.25;
            sheet.Cell(3, 1).Value = "Tank/Open";
            sheet.Cell(3, 2).Value = "Boolean";
            sheet.Cell(3, 3).Value = true;
            workbook.AddWorksheet("Notes").Cell(1, 1).Value = "just notes";
            workbook.SaveAs(path);
        }

        var result = Import(path);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Tags.Count);
        Assert.Equal(12.25, ValueConverter.Convert(result.Tags[0].Value, TagDataType.Double, false));
        Assert.Equal(true, ValueConverter.Convert(result.Tags[1].Value, TagDataType.Boolean, false));
        Assert.Contains(result.Warnings, w => w.Contains("Notes"));
    }

    [Fact]
    public void Directory_imports_all_supported_files()
    {
        WriteFile("a.csv", "Path;DataType\nA/X;Int32\n");
        WriteFile("b.json", """[ { "path": "B/Y", "dataType": "Int16" } ]""");
        WriteFile("readme.md", "ignored");

        var result = Import(_dir);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(new[] { "A/X", "B/Y" }, result.Tags.Select(t => t.Path));
    }

    [Theory]
    [InlineData("Path;DataType\nA/X;Int32\nA/X;Int32\n", "already defined")]
    [InlineData("Path;DataType\nA/X;Int32\nA/X/Y;Int32\n", "both as a variable and as a folder")]
    [InlineData("Path;DataType;Value\nA/X;Int16;100000\n", "not a valid Int16")]
    [InlineData("Path;DataType\nA/X;Decimal\n", "unknown data type")]
    [InlineData("Path;DataType;Simulation\nA/X;String;Sine\n", "not supported for String")]
    [InlineData("Path;DataType;Simulation\nA/X;Boolean;Ramp\n", "not supported for Boolean")]
    [InlineData("Path;DataType;Min;Max\nA/X;Int32;10;5\n", "greater than Max")]
    [InlineData("Path;Access\nA/X;maybe\n", "unknown access")]
    [InlineData("Path;DataType\nA//X;Int32\n", "invalid path")]
    [InlineData("Path;NodeId\nA/X;Same\nA/Y;Same\n", "already used")]
    [InlineData("DataType\nInt32\n", "missing 'Path'")]
    public void Invalid_definitions_are_reported(string csv, string expectedError)
    {
        var result = Import(WriteFile("bad.csv", csv));

        Assert.Contains(result.Errors, e => e.Contains(expectedError));
    }

    [Fact]
    public async Task Exported_csv_imports_to_the_same_tags()
    {
        TagDefinition[] tags =
        [
            new() { Path = "A/Text", DataType = TagDataType.String, Value = " Hallo; \"Welt\" ", Access = TagAccess.Read, Description = "zwei\nZeilen" },
            new() { Path = "A/Values", NodeId = "Custom.Id", DataType = TagDataType.Double, IsArray = true, Value = "1.5|2" },
            new() { Path = "B/Sine", DataType = TagDataType.Float, Simulation = SimulationKind.Sine, Min = -1.25, Max = 3, Period = 10 },
        ];
        var path = Path.Combine(_dir, "export.csv");

        await AutoOpcUaServer.Export.TagCsvWriter.WriteFileAsync(tags, path);
        var result = Import(path);

        Assert.Empty(result.Errors);
        Assert.Equal(tags.Length, result.Tags.Count);
        for (var i = 0; i < tags.Length; i++)
        {
            Assert.Equivalent(TagRowMapper.ToRow(tags[i]), TagRowMapper.ToRow(result.Tags[i]));
        }
    }

    [Fact]
    public void Missing_file_is_reported()
    {
        var result = Import(Path.Combine(_dir, "missing.csv"));

        Assert.Contains(result.Errors, e => e.Contains("not found"));
    }
}
