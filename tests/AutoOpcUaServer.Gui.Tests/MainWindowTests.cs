using System.Net;
using System.Net.Sockets;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AutoOpcUaServer.Gui.Services;
using AutoOpcUaServer.Gui.ViewModels;
using AutoOpcUaServer.Gui.Views;

namespace AutoOpcUaServer.Gui.Tests;

public sealed class MainWindowTests : IDisposable
{
    private const string Csv = """
        Path;DataType;Value;Access;Description;Simulation;Min;Max;Period
        Plant/Line1/Temperature;Double;21,5;R;Temperatur Linie 1;Sine;15;30;5
        Plant/Line1/Setpoint;Double;22;RW;Sollwert;;;;
        Plant/Line1/Running;Boolean;true;RW;Linie läuft;;;;
        Plant/Line1/PartCounter;Int32;0;R;Gefertigte Teile;Counter;0;100000;
        Plant/Line2/Mode;String;Automatik;RW;Betriebsart;;;;
        """;

    private readonly string _dir = Directory.CreateTempSubdirectory("autoopcua-gui-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private MainWindowViewModel CreateViewModel() => new(new SettingsStore(Path.Combine(_dir, "settings")))
    {
        Port = GetFreePort(),
    };

    private string WriteCsv(string content, string name = "tags.csv")
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [AvaloniaFact]
    public async Task Adding_a_file_imports_its_tags()
    {
        var vm = CreateViewModel();

        await vm.AddPathsAsync([WriteCsv(Csv)]);

        Assert.Equal(5, vm.Tags.Count);
        Assert.Equal("Tags (5)", vm.TagsHeader);
        Assert.Equal("21.5", vm.Tags[0].Value);
        Assert.Equal("Plant.Line1.Temperature", vm.Tags[0].NodeId);
        Assert.True(vm.StartCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task Import_errors_block_the_start()
    {
        var vm = CreateViewModel();

        await vm.AddPathsAsync([WriteCsv("Path;DataType\nA/X;Unknown\n")]);

        Assert.Equal(1, vm.ErrorCount);
        Assert.Empty(vm.Tags);
        Assert.False(vm.StartCommand.CanExecute(null));
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.Contains(vm.Log, l => l.IsError && l.Message.Contains("unknown data type"));
    }

    [AvaloniaFact]
    public async Task Filter_limits_visible_tags()
    {
        var vm = CreateViewModel();
        await vm.AddPathsAsync([WriteCsv(Csv)]);

        vm.Filter = "line2";

        Assert.Equal(["Plant/Line2/Mode"], vm.VisibleTags.Select(t => t.Path));
    }

    [AvaloniaFact]
    public async Task Running_server_shows_live_values_and_accepts_manual_values()
    {
        var vm = CreateViewModel();
        await vm.AddPathsAsync([WriteCsv(Csv)]);

        await vm.StartAsync();
        try
        {
            Assert.True(vm.IsRunning);
            Assert.StartsWith("opc.tcp://", vm.EndpointUrl);

            var counter = vm.Tags.Single(t => t.Path.EndsWith("PartCounter"));
            await Task.Delay(1500);
            vm.RefreshValues();
            Assert.NotEqual("0", counter.Value);
            Assert.NotEmpty(counter.Timestamp);

            vm.SelectedTag = vm.Tags.Single(t => t.Path.EndsWith("Setpoint"));
            vm.EditValue = "42,5";
            vm.SetValueCommand.Execute(null);
            Assert.Equal("42.5", vm.SelectedTag.Value);

            vm.EditValue = "abc";
            vm.SetValueCommand.Execute(null);
            Assert.Contains(vm.Log, l => l.IsError && l.Message.Contains("not a valid Double"));
        }
        finally
        {
            await vm.StopAsync();
        }

        Assert.False(vm.IsRunning);
        Assert.Equal("22", vm.Tags.Single(t => t.Path.EndsWith("Setpoint")).Value);
    }

    [AvaloniaFact]
    public async Task Settings_are_remembered()
    {
        var csv = WriteCsv(Csv);
        var vm = CreateViewModel();
        await vm.AddPathsAsync([csv]);
        vm.ApplicationName = "MeinServer";
        vm.Password = "geheim";
        vm.SaveSettings();

        var reloaded = CreateViewModel();

        Assert.Equal([csv], reloaded.Files);
        Assert.Equal("MeinServer", reloaded.ApplicationName);
        Assert.Equal("", reloaded.Password);
    }

    [AvaloniaFact]
    public async Task Main_window_renders()
    {
        var vm = CreateViewModel();
        await vm.AddPathsAsync([WriteCsv(Csv)]);
        await vm.AddManualTagAsync(new Model.TagDefinition
        {
            Path = "Plant/Line2/Alarm", DataType = Model.TagDataType.Boolean, Description = "Störmeldung", Source = "Manuell",
        });
        await vm.StartAsync();
        try
        {
            await Task.Delay(1200);
            vm.RefreshValues();
            vm.SelectedTag = vm.Tags[1];

            var window = new MainWindow { DataContext = vm, Width = 1280, Height = 720 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            // Set GUI_SCREENSHOT=<path.png> to save the rendered window, e.g. for the README.
            if (Environment.GetEnvironmentVariable("GUI_SCREENSHOT") is { Length: > 0 } path)
            {
                frame.Save(path);
            }

            window.Close();
        }
        finally
        {
            await vm.StopAsync();
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
