using System.Globalization;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoOpcUaServer.Gui.ViewModels;

/// <summary>Form for creating or editing a manually defined node (variable).</summary>
public sealed partial class NodeEditorViewModel : ObservableObject
{
    public const string ManualSource = "Manuell";

    private readonly IReadOnlyList<TagDefinition> _otherTags;
    private readonly char _separator;

    /// <param name="otherTags">All other tags of the server, used to detect conflicts.</param>
    /// <param name="existing">The node to edit, or null to create a new one.</param>
    /// <param name="suggestedFolder">Folder to prefill for a new node, e.g. the folder of the selected tag.</param>
    public NodeEditorViewModel(IReadOnlyList<TagDefinition> otherTags, TagDefinition? existing = null, string? suggestedFolder = null, char separator = '/')
    {
        _otherTags = otherTags;
        _separator = separator;
        IsNew = existing is null;

        if (existing is null)
        {
            Path = string.IsNullOrEmpty(suggestedFolder) ? "" : suggestedFolder.TrimEnd(separator) + separator;
            return;
        }

        Path = existing.Path;
        NodeId = existing.NodeId ?? "";
        DataType = existing.DataType;
        IsArray = existing.IsArray;
        Value = existing.Value ?? "";
        IsWritable = existing.Access == TagAccess.ReadWrite;
        Description = existing.Description ?? "";
        Simulation = existing.Simulation;
        Min = Format(existing.Min);
        Max = Format(existing.Max);
        Period = Format(existing.Period);
    }

    public bool IsNew { get; }

    public string Title => IsNew ? "Node anlegen" : "Node bearbeiten";

    public string SaveText => IsNew ? "Node anlegen" : "Änderungen speichern";

    public IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();

    public IReadOnlyList<SimulationKind> SimulationKinds { get; } = Enum.GetValues<SimulationKind>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NodeIdPreview))]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NodeIdPreview))]
    private string _nodeId = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private TagDataType _dataType = TagDataType.Double;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueHint))]
    private bool _isArray;

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private bool _isWritable = true;

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimulated), nameof(UsesPeriod))]
    private SimulationKind _simulation = SimulationKind.None;

    [ObservableProperty]
    private string _min = "";

    [ObservableProperty]
    private string _max = "";

    [ObservableProperty]
    private string _period = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    private string _errorText = "";

    public bool HasErrors => ErrorText.Length > 0;

    public bool IsSimulated => Simulation != SimulationKind.None;

    public bool UsesPeriod => Simulation is SimulationKind.Sine or SimulationKind.Ramp;

    /// <summary>The NodeId clients will see, shown below the path while typing.</summary>
    public string NodeIdPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Path))
            {
                return "";
            }

            var id = TagPaths.VariableNodeId(new TagDefinition { Path = Path, NodeId = NodeId }, _separator);
            return $"NodeId: ns=2;s={id}";
        }
    }

    public string ValueHint => (DataType, IsArray) switch
    {
        (_, true) => "Mehrere Werte mit | trennen, z. B. 1|2|3",
        (TagDataType.Boolean, _) => "true oder false",
        (TagDataType.DateTime, _) => "z. B. 2026-01-01T08:00:00Z, leer = jetzt",
        (TagDataType.String, _) => "Beliebiger Text",
        _ => "Leer = 0",
    };

    /// <summary>The created or changed tag after a successful save; null if cancelled.</summary>
    public TagDefinition? Result { get; private set; }

    /// <summary>Raised when the dialog should close (after save or cancel).</summary>
    public event Action? CloseRequested;

    [RelayCommand]
    public void Save()
    {
        var errors = new List<string>();
        var tag = new TagRowMapper(_separator).Map(BuildRow(), ManualSource, errors);
        if (tag is not null)
        {
            TagValidator.Validate([.. _otherTags, tag], _separator, errors, new List<string>());
        }

        if (errors.Count > 0)
        {
            ErrorText = string.Join(Environment.NewLine, errors.Select(StripSource));
            return;
        }

        ErrorText = "";
        Result = tag;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    public void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke();
    }

    private Dictionary<string, string?> BuildRow()
    {
        var row = new Dictionary<string, string?>
        {
            ["path"] = Path,
            ["nodeid"] = NodeId,
            ["datatype"] = DataType + (IsArray ? "[]" : ""),
            ["value"] = Value,
            ["access"] = IsWritable ? "RW" : "R",
            ["description"] = Description,
        };

        if (IsSimulated)
        {
            row["simulation"] = Simulation.ToString();
            row["min"] = Min;
            row["max"] = Max;
            row["period"] = UsesPeriod ? Period : null;
        }

        return row;
    }

    // Validation messages start with "Manuell (Path): " – the form already shows which node it is about.
    private static string StripSource(string message)
    {
        var colon = message.IndexOf("): ", StringComparison.Ordinal);
        if (message.StartsWith(ManualSource, StringComparison.Ordinal) && colon > 0)
        {
            return message[(colon + 3)..];
        }

        return message.StartsWith(ManualSource + ": ", StringComparison.Ordinal) ? message[(ManualSource.Length + 2)..] : message;
    }

    private static string Format(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
