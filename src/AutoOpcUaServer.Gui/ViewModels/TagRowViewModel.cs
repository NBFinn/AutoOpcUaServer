using AutoOpcUaServer.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoOpcUaServer.Gui.ViewModels;

/// <summary>One row of the tag table.</summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    public TagRowViewModel(TagDefinition tag, string nodeId, bool isManual = false)
    {
        Tag = tag;
        NodeId = nodeId;
        IsManual = isManual;
        DataType = tag.DataType + (tag.IsArray ? "[]" : "");
        Access = tag.Access == TagAccess.ReadWrite ? "RW" : "R";
        Simulation = tag.Simulation == SimulationKind.None ? "" : tag.Simulation.ToString();
        ResetValue();
    }

    public TagDefinition Tag { get; }

    /// <summary>Created in the GUI rather than imported from a file; only these can be edited.</summary>
    public bool IsManual { get; }

    public string Path => Tag.Path;

    public string NodeId { get; }

    public string DataType { get; }

    public string Access { get; }

    public string Simulation { get; }

    public string Description => Tag.Description ?? "";

    public string Source => Tag.Source;

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _timestamp = "";

    /// <summary>Shows the initial value from the import file.</summary>
    public void ResetValue()
    {
        Value = ValueFormatter.Format(ValueConverter.Convert(Tag.Value, Tag.DataType, Tag.IsArray));
        Timestamp = "";
    }

    public void Update(object? value, DateTime timestamp)
    {
        Value = ValueFormatter.Format(value);
        Timestamp = timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    public bool Matches(string filter) =>
        Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || NodeId.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || DataType.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
