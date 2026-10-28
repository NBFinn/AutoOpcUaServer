using System.Diagnostics;
using AutoOpcUaServer.Import;
using AutoOpcUaServer.Model;
using Opc.Ua;
using Opc.Ua.Server;

namespace AutoOpcUaServer.Server;

/// <summary>
/// Builds the address space from the imported tags: one folder per path segment and one
/// variable per tag. Simulated variables are updated by a timer.
/// </summary>
public sealed class TagNodeManager : CustomNodeManager2
{
    private readonly IReadOnlyList<TagDefinition> _tags;
    private readonly ServerOptions _options;
    private readonly List<(BaseDataVariableState Variable, Simulator Simulator)> _simulated = [];
    private readonly Dictionary<string, (BaseDataVariableState Variable, TagDefinition Tag)> _variables = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = new();
    private Timer? _simulationTimer;

    public TagNodeManager(IServerInternal server, ApplicationConfiguration configuration, IReadOnlyList<TagDefinition> tags, ServerOptions options)
        : base(server, configuration, options.NamespaceUri)
    {
        _tags = tags;
        _options = options;
        SystemContext.NodeIdFactory = this;
    }

    /// <summary>Number of variables created in the address space.</summary>
    public int VariableCount => _variables.Count;

    /// <summary>Raised when an OPC UA client writes a value (NodeId string, new value).</summary>
    public event Action<string, object?>? ValueWritten;

    /// <summary>Raised for problems that occur while the server is running.</summary>
    public event Action<string>? Error;

    /// <summary>Returns the current value of every variable.</summary>
    public IReadOnlyList<TagValue> ReadValues()
    {
        lock (Lock)
        {
            return _variables
                .Select(v => new TagValue(v.Key, v.Value.Variable.Value, v.Value.Variable.Timestamp))
                .ToList();
        }
    }

    /// <summary>
    /// Sets a variable from text, as if it had been written by a client. Returns an error message
    /// if the node does not exist or the text does not match the data type.
    /// </summary>
    public string? SetValue(string nodeId, string? text)
    {
        if (!_variables.TryGetValue(nodeId, out var entry))
        {
            return $"Unknown node '{nodeId}'.";
        }

        object value;
        try
        {
            value = ValueConverter.Convert(text, entry.Tag.DataType, entry.Tag.IsArray);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            return $"'{text}' is not a valid {entry.Tag.DataType}{(entry.Tag.IsArray ? "[]" : "")}.";
        }

        lock (Lock)
        {
            entry.Variable.Value = value;
            entry.Variable.Timestamp = DateTime.UtcNow;
            entry.Variable.StatusCode = StatusCodes.Good;
            entry.Variable.ClearChangeMasks(SystemContext, false);
        }

        return null;
    }

    public override NodeId New(ISystemContext context, NodeState node) => node.NodeId;

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var objectsReferences))
            {
                externalReferences[ObjectIds.ObjectsFolder] = objectsReferences = new List<IReference>();
            }

            var topLevel = new List<BaseInstanceState>();
            FolderState? root = null;
            if (!string.IsNullOrWhiteSpace(_options.RootFolder))
            {
                root = CreateFolder(null, _options.RootFolder.Trim(), _options.RootFolder.Trim());
                topLevel.Add(root);
            }

            var folders = new Dictionary<string, FolderState>(StringComparer.Ordinal);
            foreach (var tag in _tags)
            {
                var segments = TagPaths.Split(tag.Path, _options.PathSeparator);
                NodeState? parent = root;
                for (var i = 0; i < segments.Length - 1; i++)
                {
                    var folderPath = string.Join(_options.PathSeparator, segments[..(i + 1)]);
                    if (!folders.TryGetValue(folderPath, out var folder))
                    {
                        var nodeId = TagPaths.FolderNodeId(folderPath, _options.PathSeparator);
                        folder = CreateFolder(parent, nodeId, segments[i]);
                        folders[folderPath] = folder;
                        if (parent is null)
                        {
                            topLevel.Add(folder);
                        }
                    }

                    parent = folder;
                }

                var variable = CreateVariable(parent, tag);
                if (parent is null)
                {
                    topLevel.Add(variable);
                }

                if (tag.Simulation != SimulationKind.None)
                {
                    _simulated.Add((variable, new Simulator(tag)));
                }

                _variables[variable.NodeId.Identifier.ToString()!] = (variable, tag);
            }

            foreach (var node in topLevel)
            {
                node.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                objectsReferences.Add(new NodeStateReference(ReferenceTypes.Organizes, false, node.NodeId));
                if (node is FolderState folder)
                {
                    folder.EventNotifier = EventNotifiers.SubscribeToEvents;
                    AddRootNotifier(folder);
                }

                AddPredefinedNode(SystemContext, node);
            }
        }

        if (_simulated.Count > 0)
        {
            _clock.Start();
            _simulationTimer = new Timer(_ => Simulate(), null, _options.SimulationIntervalMs, _options.SimulationIntervalMs);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _simulationTimer?.Dispose();
            _simulationTimer = null;
        }

        base.Dispose(disposing);
    }

    private FolderState CreateFolder(NodeState? parent, string nodeId, string name)
    {
        var folder = new FolderState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            NodeId = new NodeId(nodeId, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(name),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None,
        };

        parent?.AddChild(folder);
        return folder;
    }

    private BaseDataVariableState CreateVariable(NodeState? parent, TagDefinition tag)
    {
        var name = TagPaths.Split(tag.Path, _options.PathSeparator)[^1];
        var accessLevel = tag.Access == TagAccess.ReadWrite ? AccessLevels.CurrentReadOrWrite : AccessLevels.CurrentRead;

        var variable = new BaseDataVariableState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
            NodeId = new NodeId(TagPaths.VariableNodeId(tag, _options.PathSeparator), NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(name),
            Description = tag.Description is null ? null : new LocalizedText(tag.Description),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            DataType = DataTypeId(tag.DataType),
            ValueRank = tag.IsArray ? ValueRanks.OneDimension : ValueRanks.Scalar,
            AccessLevel = accessLevel,
            UserAccessLevel = accessLevel,
            Historizing = false,
            Value = ValueConverter.Convert(tag.Value, tag.DataType, tag.IsArray),
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
        };

        if (tag.IsArray)
        {
            variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
        }

        if (tag.Access == TagAccess.ReadWrite)
        {
            variable.OnSimpleWriteValue = OnWriteValue;
        }

        parent?.AddChild(variable);
        return variable;
    }

    private ServiceResult OnWriteValue(ISystemContext context, NodeState node, ref object value)
    {
        ValueWritten?.Invoke(node.NodeId.Identifier.ToString()!, value);
        return ServiceResult.Good;
    }

    private void Simulate()
    {
        try
        {
            var elapsed = _clock.Elapsed.TotalSeconds;
            var now = DateTime.UtcNow;
            lock (Lock)
            {
                foreach (var (variable, simulator) in _simulated)
                {
                    variable.Value = simulator.Next(elapsed);
                    variable.Timestamp = now;
                    variable.StatusCode = StatusCodes.Good;
                    variable.ClearChangeMasks(SystemContext, false);
                }
            }
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Simulation error: {ex.Message}");
        }
    }

    private static NodeId DataTypeId(TagDataType type) => type switch
    {
        TagDataType.Boolean => DataTypeIds.Boolean,
        TagDataType.SByte => DataTypeIds.SByte,
        TagDataType.Byte => DataTypeIds.Byte,
        TagDataType.Int16 => DataTypeIds.Int16,
        TagDataType.UInt16 => DataTypeIds.UInt16,
        TagDataType.Int32 => DataTypeIds.Int32,
        TagDataType.UInt32 => DataTypeIds.UInt32,
        TagDataType.Int64 => DataTypeIds.Int64,
        TagDataType.UInt64 => DataTypeIds.UInt64,
        TagDataType.Float => DataTypeIds.Float,
        TagDataType.Double => DataTypeIds.Double,
        TagDataType.String => DataTypeIds.String,
        TagDataType.DateTime => DataTypeIds.DateTime,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };
}

/// <summary>Current value of a variable, identified by its string NodeId.</summary>
public sealed record TagValue(string NodeId, object? Value, DateTime Timestamp);
