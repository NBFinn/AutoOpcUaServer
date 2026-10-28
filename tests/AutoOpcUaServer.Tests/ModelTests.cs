using AutoOpcUaServer.Model;
using AutoOpcUaServer.Server;

namespace AutoOpcUaServer.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("double", TagDataType.Double, false)]
    [InlineData("REAL", TagDataType.Float, false)]
    [InlineData("int[]", TagDataType.Int32, true)]
    [InlineData(" UInt16 [] ", TagDataType.UInt16, true)]
    [InlineData("bool", TagDataType.Boolean, false)]
    public void Data_type_names_are_parsed(string text, TagDataType expected, bool expectedArray)
    {
        Assert.True(TagDataTypes.TryParse(text, out var type, out var isArray));
        Assert.Equal(expected, type);
        Assert.Equal(expectedArray, isArray);
    }

    [Fact]
    public void Values_are_converted()
    {
        Assert.Equal(1.5, ValueConverter.Convert("1,5", TagDataType.Double, false));
        Assert.Equal(1234.5, ValueConverter.Convert("1234.5", TagDataType.Double, false));
        Assert.Equal((byte)0, ValueConverter.Convert("", TagDataType.Byte, false));
        Assert.Equal(false, ValueConverter.Convert("nein", TagDataType.Boolean, false));
        Assert.Equal(new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), ValueConverter.Convert("2026-01-01T08:00:00Z", TagDataType.DateTime, false));
        Assert.Equal(Array.Empty<string>(), ValueConverter.Convert("", TagDataType.String, true));
        Assert.Throws<FormatException>(() => ValueConverter.Convert("abc", TagDataType.Int32, false));
    }

    [Fact]
    public void Simulated_values_are_rounded_and_clamped()
    {
        Assert.Equal((byte)255, ValueConverter.FromDouble(300, TagDataType.Byte));
        Assert.Equal(3, ValueConverter.FromDouble(2.6, TagDataType.Int32));
        Assert.Equal(2.5f, ValueConverter.FromDouble(2.5, TagDataType.Float));
    }

    [Fact]
    public void Simulators_stay_within_range()
    {
        var kinds = new[] { SimulationKind.Random, SimulationKind.Sine, SimulationKind.Ramp, SimulationKind.Counter, SimulationKind.Toggle };
        foreach (var kind in kinds)
        {
            var simulator = new Simulator(new TagDefinition { Path = "X", DataType = TagDataType.Int16, Simulation = kind, Min = -5, Max = 5, Period = 3 });
            for (var t = 0; t < 50; t++)
            {
                var value = Assert.IsType<short>(simulator.Next(t * 0.7));
                Assert.InRange(value, (short)-5, (short)5);
            }
        }
    }

    [Fact]
    public void Counter_wraps_and_toggle_alternates()
    {
        var counter = new Simulator(new TagDefinition { Path = "C", DataType = TagDataType.Int32, Simulation = SimulationKind.Counter, Min = 0, Max = 2 });
        Assert.Equal(new object[] { 1, 2, 0, 1 }, Enumerable.Range(0, 4).Select(i => counter.Next(i)).ToArray());

        var toggle = new Simulator(new TagDefinition { Path = "T", DataType = TagDataType.Boolean, Simulation = SimulationKind.Toggle });
        Assert.Equal(new object[] { true, false, true }, Enumerable.Range(0, 3).Select(i => toggle.Next(i)).ToArray());
    }

    [Fact]
    public void Command_line_is_parsed()
    {
        var options = CommandLine.Parse(["tags.csv", "-p", "4841", "--security", "none", "--user", "admin:se:cret", "--root", "Tags"], out var error, out _);

        Assert.Null(error);
        Assert.NotNull(options);
        Assert.Equal(["tags.csv"], options.InputPaths);
        Assert.Equal(4841, options.Port);
        Assert.Equal(SecurityMode.None, options.Security);
        Assert.Equal("admin", options.UserName);
        Assert.Equal("se:cret", options.Password);
        Assert.Equal("Tags", options.RootFolder);
        Assert.Equal("opc.tcp://localhost:4841", options.EndpointUrl);
    }

    [Theory]
    [InlineData("--port", "99999")]
    [InlineData("--security", "1")]
    [InlineData("--unknown")]
    public void Invalid_command_lines_are_rejected(params string[] args)
    {
        Assert.Null(CommandLine.Parse(["tags.csv", .. args], out var error, out _));
        Assert.NotNull(error);
    }
}
