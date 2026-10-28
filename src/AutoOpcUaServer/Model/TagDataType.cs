namespace AutoOpcUaServer.Model;

/// <summary>Data types that can be used in import files. Names match the OPC UA built-in types.</summary>
public enum TagDataType
{
    Boolean,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float,
    Double,
    String,
    DateTime,
}

public static class TagDataTypes
{
    private static readonly Dictionary<string, TagDataType> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bool"] = TagDataType.Boolean,
        ["boolean"] = TagDataType.Boolean,
        ["sbyte"] = TagDataType.SByte,
        ["byte"] = TagDataType.Byte,
        ["short"] = TagDataType.Int16,
        ["int16"] = TagDataType.Int16,
        ["ushort"] = TagDataType.UInt16,
        ["uint16"] = TagDataType.UInt16,
        ["word"] = TagDataType.UInt16,
        ["int"] = TagDataType.Int32,
        ["int32"] = TagDataType.Int32,
        ["dint"] = TagDataType.Int32,
        ["uint"] = TagDataType.UInt32,
        ["uint32"] = TagDataType.UInt32,
        ["dword"] = TagDataType.UInt32,
        ["long"] = TagDataType.Int64,
        ["int64"] = TagDataType.Int64,
        ["lint"] = TagDataType.Int64,
        ["ulong"] = TagDataType.UInt64,
        ["uint64"] = TagDataType.UInt64,
        ["float"] = TagDataType.Float,
        ["single"] = TagDataType.Float,
        ["real"] = TagDataType.Float,
        ["double"] = TagDataType.Double,
        ["lreal"] = TagDataType.Double,
        ["string"] = TagDataType.String,
        ["datetime"] = TagDataType.DateTime,
    };

    /// <summary>
    /// Parses a data type name such as "Double", "int" or "Float[]". A trailing "[]" marks an array.
    /// </summary>
    public static bool TryParse(string? text, out TagDataType type, out bool isArray)
    {
        type = TagDataType.Double;
        isArray = false;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var name = text.Trim();
        if (name.EndsWith("[]", StringComparison.Ordinal))
        {
            isArray = true;
            name = name[..^2].TrimEnd();
        }

        return Aliases.TryGetValue(name, out type);
    }

    public static bool IsNumeric(TagDataType type) =>
        type is not (TagDataType.Boolean or TagDataType.String or TagDataType.DateTime);

    public static bool IsInteger(TagDataType type) =>
        IsNumeric(type) && type is not (TagDataType.Float or TagDataType.Double);
}
