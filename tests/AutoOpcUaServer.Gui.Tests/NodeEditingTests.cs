using System.Net;
using System.Net.Sockets;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Gui.ViewModels;
using AutoOpcUaServer.Gui.Views;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Gui.Tests;

public sealed class NodeEditingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("autoopcua-nodes-").FullName;
    private readonly FakeUi _ui = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainWindowViewModel CreateViewModel() => new(new SettingsStore(Path.Combine(_dir, "settings")))
    {
        Port = GetFreePort(),
        Ui = _ui,
    };

    private async Task<MainWindowViewModel> CreateWithCsvAsync()
    {
        var csv = Path.Combine(_dir, "tags.csv");
        await File.WriteAllTextAsync(csv, "Path;DataType;Value\nPlant/Line1/Temp;Double;20\n");
        var vm = CreateViewModel();
        await vm.AddPathsAsync([csv]);
        return vm;
    }

    [AvaloniaFact]
    public async Task Node_can_be_added_and_is_remembered()
    {
        var vm = await CreateWithCsvAsync();
        vm.SelectedTag = vm.Tags[0];
        _ui.FillEditor = e =>
        {
            e.Path += "Setpoint";
            e.DataType = TagDataType.Int16;
            e.Value = "5";
        };

        await vm.AddNodeCommand.ExecuteAsync(null);

        Assert.Equal("Plant/Line1/", _ui.LastEditor!.Path[..12]);
        var row = Assert.Single(vm.Tags, t => t.IsManual);
        Assert.Equal("Plant/Line1/Setpoint", row.Path);
        Assert.Equal("Manuell", row.Source);
        Assert.Same(row, vm.SelectedTag);

        var reloaded = CreateViewModel();
        reloaded.Initialize([]);
        await reloaded.ReloadAsync();
        Assert.Contains(reloaded.Tags, t => t.IsManual && t.Path == "Plant/Line1/Setpoint" && t.Value == "5");
    }

    [AvaloniaFact]
    public void Editor_reports_invalid_input()
    {
        var existing = new TagDefinition { Path = "Plant/Temp", Source = "tags.csv:2" };
        var editor = new NodeEditorViewModel([existing]) { Path = "Plant/New", DataType = TagDataType.Int32, Value = "abc" };

        editor.Save();

        Assert.Null(editor.Result);
        Assert.True(editor.HasErrors);
        Assert.Contains("not a valid Int32", editor.ErrorText);
        Assert.DoesNotContain("Manuell", editor.ErrorText);

        editor.Value = "1";
        editor.Path = "Plant/Temp";
        editor.Save();
        Assert.Contains("already defined in tags.csv:2", editor.ErrorText);

        editor.Path = "Plant/Other";
        Assert.Equal("NodeId: ns=2;s=Plant.Other", editor.NodeIdPreview);
        editor.Save();
        Assert.False(editor.HasErrors);
        Assert.Equal(1, ValueConverter.Convert(editor.Result!.Value, TagDataType.Int32, false));
    }

    [AvaloniaFact]
    public async Task Imported_nodes_cannot_be_edited_manual_nodes_can()
    {
        var vm = await CreateWithCsvAsync();
        await vm.AddManualTagAsync(new TagDefinition { Path = "Extra/Flag", DataType = TagDataType.Boolean, Source = "Manuell" });

        vm.SelectedTag = vm.Tags.Single(t => !t.IsManual);
        Assert.False(vm.EditNodeCommand.CanExecute(null));
        Assert.False(vm.DeleteNodeCommand.CanExecute(null));

        vm.SelectedTag = vm.Tags.Single(t => t.IsManual);
        _ui.FillEditor = e => e.Simulation = SimulationKind.Toggle;
        await vm.EditNodeCommand.ExecuteAsync(null);
        Assert.Equal("Toggle", vm.Tags.Single(t => t.IsManual).Simulation);

        vm.DeleteNodeCommand.Execute(null);
        Assert.DoesNotContain(vm.Tags, t => t.IsManual);
    }

    [AvaloniaFact]
    public async Task Server_starts_with_manual_nodes_only_and_accepts_nodes_while_running()
    {
        var vm = CreateViewModel();
        await vm.AddManualTagAsync(new TagDefinition { Path = "Demo/Counter", DataType = TagDataType.Int32, Simulation = SimulationKind.Counter, Source = "Manuell" });
        Assert.True(vm.StartCommand.CanExecute(null));

        await vm.StartAsync();
        try
        {
            var error = await vm.AddManualTagAsync(new TagDefinition { Path = "Demo/Live", DataType = TagDataType.String, Value = "neu", Source = "Manuell" });
            Assert.Null(error);

            vm.SelectedTag = vm.Tags.Single(t => t.Path == "Demo/Live");
            Assert.False(vm.EditNodeCommand.CanExecute(null));
            vm.EditValue = "geändert";
            vm.SetValueCommand.Execute(null);
            Assert.Equal("geändert", vm.SelectedTag!.Value);

            var duplicate = await vm.AddManualTagAsync(new TagDefinition { Path = "Demo/Live", Source = "Manuell" });
            Assert.Contains("already defined", duplicate);
            Assert.Equal(2, vm.Tags.Count);

            await Task.Delay(1200);
            vm.RefreshValues();
            Assert.NotEqual("0", vm.Tags.Single(t => t.Path == "Demo/Counter").Value);
        }
        finally
        {
            await vm.StopAsync();
        }
    }

    [AvaloniaFact]
    public async Task Export_writes_a_csv_that_can_be_imported_again()
    {
        var vm = await CreateWithCsvAsync();
        await vm.AddManualTagAsync(new TagDefinition { Path = "Extra/Values", DataType = TagDataType.Float, IsArray = true, Value = "1.5|2", Source = "Manuell" });
        _ui.SavePath = Path.Combine(_dir, "export.csv");

        await vm.ExportCommand.ExecuteAsync(null);
        var result = new TagImporter().Import([_ui.SavePath]);

        Assert.Empty(result.Errors);
        Assert.Equal(["Plant/Line1/Temp", "Extra/Values"], result.Tags.Select(t => t.Path));
    }

    [AvaloniaFact]
    public void Editor_window_renders()
    {
        var editor = new NodeEditorViewModel([new TagDefinition { Path = "Anlage/Linie1/Druck" }], suggestedFolder: "Anlage/Linie1")
        {
            DataType = TagDataType.Double,
            Value = "21,5",
            Description = "Temperatur am Einlauf in °C",
            Simulation = SimulationKind.Sine,
            Min = "15",
            Max = "30",
            Period = "60",
        };
        editor.Path += "Temperatur";

        var window = new NodeEditorWindow(editor);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        if (Environment.GetEnvironmentVariable("GUI_SCREENSHOT_EDITOR") is { Length: > 0 } path)
        {
            frame.Save(path);
        }

        window.Close();
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
