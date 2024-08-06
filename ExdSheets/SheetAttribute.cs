namespace ExdSheets;

[AttributeUsage(AttributeTargets.Struct)]
public class SheetAttribute(string name, uint columnHash = uint.MaxValue) : Attribute
{
    public readonly string Name = name;
    public readonly uint? ColumnHash = columnHash == uint.MaxValue ? null : columnHash;
}
