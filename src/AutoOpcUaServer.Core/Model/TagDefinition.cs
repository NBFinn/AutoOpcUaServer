namespace AutoOpcUaServer.Model;

/// <summary>Access rights of a variable as seen by OPC UA clients.</summary>
public enum TagAccess
{
    Read,
    ReadWrite,
}

/// <summary>How the value of a variable changes over time.</summary>
public enum SimulationKind
{
    None,
    Random,
    Sine,
    Ramp,
    Counter,
    Toggle,
}

/// <summary>
/// One variable that should be exposed by the server, as read from an import file.
/// </summary>
public sealed class TagDefinition
{
    /// <summary>Folder path plus variable name, e.g. "Plant/Line1/Temperature".</summary>
    public required string Path { get; init; }

    /// <summary>Optional explicit string identifier for the NodeId (ns=&lt;tags&gt;;s=...).</summary>
    public string? NodeId { get; init; }

    public TagDataType DataType { get; init; } = TagDataType.Double;

    public bool IsArray { get; init; }

    /// <summary>Raw initial value as text. Array elements are separated by '|'.</summary>
    public string? Value { get; init; }

    public TagAccess Access { get; init; } = TagAccess.ReadWrite;

    public string? Description { get; init; }

    public SimulationKind Simulation { get; init; } = SimulationKind.None;

    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>Period in seconds for Sine and Ramp simulations.</summary>
    public double? Period { get; init; }

    /// <summary>Where this tag came from ("tags.csv:12"), used in error messages.</summary>
    public string Source { get; init; } = "";
}
