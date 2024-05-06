using ExdGenerator.Schema;
using Lumina;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace ExdGenerator;

public class SchemaSourceConverter
{
    private Sheet Definition { get; }
    private GameData GameData { get; }
    private RawExcelSheet GameSheet { get; }
    private string? ReferencedSheetNamespace { get; }

    public string ParseCode { get; }
    public string DefinitionCode { get; }

    public string SheetName { get; }
    public uint ColumnHash { get; }

    public SchemaSourceConverter(Sheet sheetDefinition, GameData gameData, string? referencedSheetNamespace)
    {
        Definition = sheetDefinition;
        GameData = gameData;

        GameSheet = GameData.Excel.GetSheetRaw(Definition.Name) ?? throw new InvalidOperationException($"Sheet {Definition.Name} not found in game data");

        if (string.IsNullOrEmpty(referencedSheetNamespace))
            ReferencedSheetNamespace = null;
        else
        {
            if (!referencedSheetNamespace!.StartsWith("global::"))
                referencedSheetNamespace = $"global::{referencedSheetNamespace}";
            if (referencedSheetNamespace.EndsWith('.'))
                referencedSheetNamespace = referencedSheetNamespace[..^1];
            ReferencedSheetNamespace = referencedSheetNamespace;
        }

        SheetName = GameSheet.Name;
        ColumnHash = GameSheet.HeaderFile.GetColumnsHash();

        var orderedColumns = GameSheet.Columns.GroupBy(c => c.Offset).OrderBy(c => c.Key).SelectMany(g => g.OrderBy(c => c.Type)).ToList();

        (ParseCode, DefinitionCode) = ParseFields(Definition.Fields, Definition.Relations, orderedColumns, 0, out var offset, out _);
        if (offset != orderedColumns.Count)
            throw new InvalidOperationException($"Expected {orderedColumns.Count} columns, but only parsed {offset}");
    }

    private class RelationInfo
    {
        public string Name { get; }
        public List<string> FieldNames { get; }
        public StringBuilder DefinitionBuilder { get; }
        public int? ArraySize { get; private set; }
        public string RelationType => $"{Name}Struct";
        private bool PushedProperty { get; set; }

        public RelationInfo(KeyValuePair<string, List<string>> relation)
        {
            Name = relation.Key;
            FieldNames = relation.Value;
            DefinitionBuilder = new StringBuilder();
        }

        public void MarkArraySize(StringBuilder pb, string pIndent, string fieldPrefix, string classPrefix, int size)
        {
            if (ArraySize.HasValue && ArraySize.Value != size)
                throw new InvalidOperationException("Related array size mismatch");
            if (!ArraySize.HasValue)
            {
                ArraySize = size;
                pb.AppendLine($"{pIndent}{fieldPrefix}.{Name} = new {classPrefix}{RelationType}[{size}];");
                pb.AppendLine($"{pIndent}for (var idx = 0; idx < {size}; idx++)");
                pb.AppendLine($"{pIndent}    {fieldPrefix}.{Name}[idx] = new();");
            }
        }

        public void PushProperty(StringBuilder db, string dIndent)
        {
            if (!PushedProperty)
            {
                db.AppendLine($"{dIndent}public {RelationType}[] {Name} {{ get; internal set; }}");
                PushedProperty = true;
            }
        }
    }

    private (string ParseCode, string DefinitionCode) ParseFields(IEnumerable<Field> fields, IEnumerable<KeyValuePair<string, List<string>>>? relations, IReadOnlyList<ExcelColumnDefinition> columns, int columnIdxOffset, out int finalColumnIdxOffset, out string? wrappedType, char iterVariable = 'i', int parseIndentCount = 12, string offsetPrefix = "", string classPrefix = "", string fieldPrefix = "this")
    {
        relations ??= [];

        var pIndent = new string(' ', parseIndentCount);
        var pbBase = new StringBuilder();

        var dIndent = new string(' ', parseIndentCount - 4);
        var dbBase = new StringBuilder();

        var relationDefs = relations.ToDictionary(r => r.Key, r => new RelationInfo(r));

        var parseSnippets = new Dictionary<Field, string>();

        wrappedType = null;
        var byteOffset = 0;
        foreach (var field in fields)
        {
            var hasName = !string.IsNullOrEmpty(field.Name);
            var prefixedName = !hasName ? (fieldPrefix[^1] == '.' ? fieldPrefix[..^1] : fieldPrefix) : $"{fieldPrefix}.{field.Name}";
            var prefixedOffsetPrefix = string.IsNullOrEmpty(offsetPrefix) ? "" : $"{offsetPrefix} + ";

            var db = dbBase;
            var relationName = (hasName ? relations.FirstOrDefault(r => r.Value.Contains(field.Name!)) : default).Key;
            var isRelation = !string.IsNullOrEmpty(relationName);
            if (isRelation)
            {
                var relDef = relationDefs[relationName];
                db = relDef.DefinitionBuilder;
                prefixedName = $"{fieldPrefix}.{relationName}";

                if (field.Type != FieldType.Array)
                    throw new InvalidOperationException("Relation field must be an array");
                relDef.MarkArraySize(pbBase, pIndent, fieldPrefix, classPrefix, field.Count ?? 1);
            }

            var pb = new StringBuilder();

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
                                    pb.AppendLine($"{pIndent}    {val} => new global::Lumina.Excel.LazyRow<{DecorateReferencedType(targets[0])}>(gameData, {fieldRow}, language),");
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
                                fieldTypeName = $"global::Lumina.Excel.LazyRow<{DecorateReferencedType(field.Targets[0])}>";
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

                        var subfieldPrefix = isRelation ? $"{prefixedName}[{iterVariable}].{field.Name}" : $"{prefixedName}[{iterVariable}]";
                        var subclassPrefix = $"{classPrefix}{field.Name}Struct.";
                        var (fieldParseCode, fieldDefCode) = ParseFields(subfields, field.Relations, columns, columnIdxOffset, out _, out var fieldWrappedType, (char)(iterVariable + 1), parseIndentCount + 4, $"{prefixedOffsetPrefix}{byteOffset} + {iterVariable} * {size.byteSize}", subclassPrefix, subfieldPrefix);

                        if (string.IsNullOrEmpty(fieldWrappedType) == string.IsNullOrEmpty(fieldDefCode))
                            throw new InvalidOperationException("Array field must have either all named or one unnamed field");

                        var isSubtype = string.IsNullOrEmpty(fieldWrappedType);
                        if (isSubtype)
                        {
                            if (string.IsNullOrEmpty(field.Name))
                                throw new InvalidOperationException("Array field must have a name attached");
                            var tb = new StringBuilder();
                            tb.AppendLine($"{dIndent}public class {field.Name}Struct");
                            tb.AppendLine($"{dIndent}{{");
                            tb.AppendLine(fieldDefCode);
                            tb.AppendLine($"{dIndent}}}");
                            db.AppendLine(tb.ToString().TrimEnd());
                            fieldWrappedType = $"{classPrefix}{field.Name}Struct";
                        }

                        if (!isRelation)
                            fieldTypeName = $"{fieldWrappedType!}[]";
                        else
                            fieldTypeName = fieldWrappedType!;

                        if (!isRelation)
                            pb.AppendLine($"{pIndent}{prefixedName} = new {fieldWrappedType}[{fieldCount}];");
                        pb.AppendLine($"{pIndent}for (var {iterVariable} = 0; {iterVariable} < {fieldCount}; {iterVariable}++)");
                        pb.AppendLine($"{pIndent}{{");
                        if (!isRelation && isSubtype)
                            pb.AppendLine($"{pIndent}    {prefixedName}[{iterVariable}] = new();");
                        pb.AppendLine(fieldParseCode);
                        pb.AppendLine($"{pIndent}}}");

                        columnIdxOffset += size.columnCount * fieldCount;
                        byteOffset += size.byteSize * fieldCount;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown field type {field.Type}");
            }

            parseSnippets.Add(field, pb.ToString());

            if (hasName)
            {
                if (isRelation)
                    db.Append("    ");
                db.AppendLine($"{dIndent}public {fieldTypeName} {field.Name} {{ get; internal set; }}");
            }
            else
                wrappedType = fieldTypeName;

            if (isRelation)
                relationDefs[relationName].PushProperty(dbBase, dIndent);
        }

        var orderedParseFields = fieldPrefix == "this" ? OrderFieldDependencies(fields) : fields;

        foreach(var field in orderedParseFields)
            pbBase.Append(parseSnippets[field]);

        foreach(var relDef in relationDefs.Values)
        {
            var tb = new StringBuilder();
            tb.AppendLine($"{dIndent}public class {relDef.RelationType}");
            tb.AppendLine($"{dIndent}{{");
            tb.AppendLine(relDef.DefinitionBuilder.ToString().TrimEnd());
            tb.AppendLine($"{dIndent}}}");
            dbBase.AppendLine(tb.ToString().TrimEnd());
        }

        finalColumnIdxOffset = columnIdxOffset;
        return (pbBase.ToString().TrimEnd(), dbBase.ToString().TrimEnd());
    }

    // Topological sort
    private IEnumerable<Field> OrderFieldDependencies(IEnumerable<Field> fields)
    {
        var sorted = new List<Field>();
        var visited = new HashSet<Field>();

        void Visit(Field field)
        {
            if (visited.Contains(field))
            {
                if (!sorted.Contains(field))
                    throw new InvalidOperationException("Circular dependency detected");
                return;
            }

            visited.Add(field);

            foreach (var dep in GetDependencies(field))
                Visit(dep);

            sorted.Add(field);
        }

        IEnumerable<Field> GetDependencies(Field field)
        {
            if (field.Type == FieldType.Array)
                return field.Fields?.SelectMany(GetDependencies) ?? [];
            else if (field.Type == FieldType.Link && field.Condition is { Switch: var fieldName } && !string.IsNullOrEmpty(fieldName))
                return [fields.First(f => f.Name! == fieldName)];
            else
                return [];
        }

        foreach (var item in fields)
            Visit(item);

        return sorted;
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

    private string DecorateReferencedType(string typeName) =>
        ReferencedSheetNamespace == null ? typeName : $"{ReferencedSheetNamespace}.{typeName}";

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
