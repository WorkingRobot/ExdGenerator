using ExdGenerator.Schema;
using Lumina;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ExdGenerator;

public class SchemaSourceConverter
{
    private Sheet Definition { get; }
    private GameData GameData { get; }
    private RawExcelSheet GameSheet { get; }

    public string ParseCode { get; }
    public string DefinitionCode { get; }

    public string SheetName { get; }
    public uint ColumnHash { get; }

    public SchemaSourceConverter(Sheet sheetDefinition, GameData gameData)
    {
        Definition = sheetDefinition;
        GameData = gameData;

        GameSheet = GameData.Excel.GetSheetRaw(Definition.Name) ?? throw new InvalidOperationException($"Sheet {Definition.Name} not found in game data");

        SheetName = GameSheet.Name;
        ColumnHash = GameSheet.HeaderFile.GetColumnsHash();

        var orderedColumns = GameSheet.Columns.GroupBy(c => c.Offset).OrderBy(c => c.Key).SelectMany(g => g.OrderBy(c => c.Type)).ToList();

        (ParseCode, DefinitionCode) = ParseFields(Definition.Fields, orderedColumns, 0, out var offset, out _);
        if (offset != orderedColumns.Count)
            throw new InvalidOperationException($"Expected {orderedColumns.Count} columns, but only parsed {offset}");
    }

    private (string ParseCode, string DefinitionCode) ParseFields(IEnumerable<Field> fields, IReadOnlyList<ExcelColumnDefinition> columns, int columnIdxOffset, out int finalColumnIdxOffset, out string? wrappedType, char iterVariable = 'i', int parseIndentCount = 12, string offsetPrefix = "", string fieldPrefix = "this")
    {
        var pIndent = new string(' ', parseIndentCount);
        var pb = new StringBuilder();

        var dIndent = new string(' ', parseIndentCount - 4);
        var db = new StringBuilder();

        wrappedType = null;
        var byteOffset = 0;
        foreach (var field in fields)
        {
            var hasName = !string.IsNullOrEmpty(field.Name);
            var prefixedName = !hasName ? (fieldPrefix[^1] == '.' ? fieldPrefix[..^1] : fieldPrefix) : $"{fieldPrefix}.{field.Name}";
            var prefixedOffsetPrefix = string.IsNullOrEmpty(offsetPrefix) ? "" : $"{offsetPrefix} + ";

            string fieldTypeName;

            switch (field.Type)
            {
                case FieldType.Scalar:
                case FieldType.Icon:
                case FieldType.Color:
                case FieldType.ModelId:
                    {
                        var column = columns[columnIdxOffset];
                        var colSize = GetColumnSize(columns, columnIdxOffset);
                        var typeName = LookupTypeName(column.Type);
                        pb.AppendLine($"{pIndent}{prefixedName} = parser.ReadOffset<{typeName}>({prefixedOffsetPrefix}{byteOffset}, {GetGlobalName<ExcelColumnDataType>()}.{column.Type});");
                        fieldTypeName = typeName;
                        columnIdxOffset++;
                        byteOffset += colSize;
                    }
                    break;
                case FieldType.Link:
                    {
                        var column = columns[columnIdxOffset];
                        var colSize = GetColumnSize(columns, columnIdxOffset);
                        var fieldRow = $"checked((uint)parser.ReadOffset<{LookupTypeName(column.Type)}>({prefixedOffsetPrefix}{byteOffset}, {GetGlobalName<ExcelColumnDataType>()}.{column.Type}))";
                        if (field.Targets == null)
                        {
                            if (field.Condition == null)
                                throw new InvalidOperationException($"Field {field.Name} has no targets or condition");

                            pb.AppendLine($"{pIndent}{prefixedName} = this.{field.Condition.Switch} switch");
                            pb.AppendLine($"{pIndent}{{");
                            foreach (var (val, targets) in field.Condition.Cases!)
                            {
                                if (targets.Count == 1)
                                    pb.AppendLine($"{pIndent}    {val} => new global::Lumina.Excel.LazyRow<{targets[0]}>(gameData, {fieldRow}, language),");
                                else
                                    pb.AppendLine($"{pIndent}    {val} => {GetGlobalName<EmptyLazyRow>()}.GetFirstLazyRowOrEmpty(gameData, {fieldRow}, language, {string.Join(", ", targets.Select(GeneratorUtils.EscapeStringToken))}),");
                            }
                            pb.AppendLine($"{pIndent}    _ => new {GetGlobalName<EmptyLazyRow>()}({fieldRow}),");
                            pb.AppendLine($"{pIndent}}};");

                            fieldTypeName = GetGlobalName<ILazyRow>();
                        }
                        else
                        {
                            if (field.Targets.Count == 1)
                            {
                                fieldTypeName = $"global::Lumina.Excel.LazyRow<{field.Targets[0]}>";
                                pb.AppendLine($"{pIndent}{prefixedName} = new {fieldTypeName}(gameData, {fieldRow}, language);");
                            }
                            else
                            {
                                fieldTypeName = GetGlobalName<ILazyRow>();
                                pb.AppendLine($"{pIndent}{prefixedName} = {GetGlobalName<EmptyLazyRow>()}.GetFirstLazyRowOrEmpty(gameData, {fieldRow}, language, {string.Join(", ", field.Targets.Select(GeneratorUtils.EscapeStringToken))});");
                            }
                        }
                        columnIdxOffset++;
                        byteOffset += colSize;
                    }
                    break;
                case FieldType.Array:
                    {
                        var subfields = field.Fields ?? [new Field() { Type = FieldType.Scalar }];
                        var fieldCount = field.Count ?? 1;
                        var isSingular = string.IsNullOrEmpty(subfields[0].Name);
                        
                        var column = columns[columnIdxOffset];
                        var size = GetStructSize(subfields, columns, columnIdxOffset);

                        var (fieldParseCode, fieldDefCode) = ParseFields(subfields, columns, columnIdxOffset, out _, out var fieldWrappedType, (char)(iterVariable + 1), parseIndentCount + 4, $"{prefixedOffsetPrefix}{byteOffset} + {iterVariable} * {size.byteSize}", $"{prefixedName}[{iterVariable}]");

                        if (string.IsNullOrEmpty(fieldWrappedType) == string.IsNullOrEmpty(fieldDefCode))
                            throw new InvalidOperationException("Array field must have either all named or one unnamed field");

                        if (string.IsNullOrEmpty(fieldWrappedType))
                        {
                            if (string.IsNullOrEmpty(field.Name))
                                throw new InvalidOperationException("Array field must have a name attached");
                            fieldWrappedType = $"{field.Name}Struct";
                            var tb = new StringBuilder();
                            tb.AppendLine($"{dIndent}public class {fieldWrappedType}");
                            tb.AppendLine($"{dIndent}{{");
                            tb.AppendLine(fieldDefCode);
                            tb.AppendLine($"{dIndent}}}");
                            db.AppendLine(tb.ToString().TrimEnd());
                        }

                        fieldTypeName = $"{fieldWrappedType!}[]";

                        pb.AppendLine($"{pIndent}{prefixedName} = new {fieldWrappedType}[{fieldCount}];");
                        pb.AppendLine($"{pIndent}for (var {iterVariable} = 0; {iterVariable} < {fieldCount}; {iterVariable}++)");
                        pb.AppendLine($"{pIndent}{{");
                        pb.AppendLine(fieldParseCode);
                        pb.AppendLine($"{pIndent}}}");

                        columnIdxOffset += size.columnCount * fieldCount;
                        byteOffset += size.byteSize * fieldCount;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown field type {field.Type}");
            }

            if (hasName)
                db.AppendLine($"{dIndent}public {fieldTypeName} {field.Name} {{ get; internal set; }}");
            else
                wrappedType = fieldTypeName;
        }

        finalColumnIdxOffset = columnIdxOffset;
        return (pb.ToString().TrimEnd(), db.ToString().TrimEnd());
    }

    private (int byteSize, int columnCount) GetStructSize(IEnumerable<Field> fields, IReadOnlyList<ExcelColumnDefinition> columns, int columnIdxOffset)
    {
        var byteSize = 0;
        var columnCount = 0;
        foreach(var field in fields)
        {
            if (field.Type == FieldType.Array)
            {
                var s = GetStructSize(field.Fields ?? [new Field() { Type = FieldType.Scalar }], columns, columnIdxOffset + columnCount);
                var fieldCount = field.Count ?? 1;
                byteSize += s.byteSize * fieldCount;
                columnCount += s.columnCount * fieldCount;
            }
            else
            {
                byteSize += GetColumnSize(columns, columnIdxOffset + columnCount);
                columnCount++;
            }
        }
        return (byteSize, columnCount);
    }

    private int GetColumnSize(IReadOnlyList<ExcelColumnDefinition> columns, int offset)
    {
        var column = columns[offset];
        if (columns.Count > offset + 1)
            return columns[offset + 1].Offset - column.Offset;
        return 0;
    }

    private static string LookupTypeName(ExcelColumnDataType type) =>
        type switch
    {
        ExcelColumnDataType.String => GetGlobalName<SeString>(),
        ExcelColumnDataType.Bool => "bool",
        ExcelColumnDataType.Int8 => "sbyte",
        ExcelColumnDataType.UInt8 => "byte",
        ExcelColumnDataType.Int16 => "short",
        ExcelColumnDataType.UInt16 => "ushort",
        ExcelColumnDataType.Int32 => "int",
        ExcelColumnDataType.UInt32 => "uint",
        ExcelColumnDataType.Float32 => "float",
        ExcelColumnDataType.Int64 => "long",
        ExcelColumnDataType.UInt64 => "ulong",
        >= ExcelColumnDataType.PackedBool0 and <= ExcelColumnDataType.PackedBool7 => "bool",
        var n => throw new InvalidOperationException($"Unknown column type {n}")
    };

    private static string GetGlobalName<T>() => $"global::{typeof(T).FullName}";
}

public record TypedField
{
    public Field Field;

    public ExcelColumnDefinition? Type;

    public List<TypedField>? Subfields;

    public int? StructIndex;

    public TypedField(IEnumerator<ExcelColumnDefinition> columns, IEnumerator<Field> fields, ref int structIdx)
    {
        Field = fields.Current;

        if (Field.Type == FieldType.Array)
        {
            Subfields = [];

            Field.Fields ??= [new() { Name = "__item", Type = FieldType.Scalar }];

            if (Field.Fields.Count > 1)
                StructIndex = structIdx++;

            var fieldEnumerator = Field.Fields!.GetEnumerator();
            for (var i = 0; i < Field.Fields!.Count; i++)
            {
                Subfields.Add(new TypedField(columns, fieldEnumerator, ref structIdx));
                fieldEnumerator.MoveNext();
            }
        }
        else
        {
            Type = columns.Current;
            columns.MoveNext();
        }
    }
}
