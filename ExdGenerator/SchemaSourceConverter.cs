using ExdGenerator.Schema;
using Lumina;
using Lumina.Data.Structs.Excel;
using Lumina.Excel;
using Lumina.Text;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExdGenerator;

public class SchemaSourceConverter
{
    public Sheet Definition { get; }
    public GameData GameData { get; }
    public TypeGlobalizer TypeGlobalizer { get; }
    public string IndentString { get; }
    private RawExcelSheet GameSheet { get; }
    private string? ReferencedSheetNamespace { get; }

    public string ParseCode { get; }
    public string DefinitionCode { get; }

    public string SheetName { get; }
    public uint ColumnHash { get; }

    public SchemaSourceConverter(Sheet sheetDefinition, GameData gameData, TypeGlobalizer typeGlobalizer, string indentString, bool useThis, string? referencedSheetNamespace)
    {
        Definition = sheetDefinition;
        GameData = gameData;
        TypeGlobalizer = typeGlobalizer;
        IndentString = indentString;

        GameSheet = GameData.Excel.GetSheetRaw(Definition.Name) ?? throw new InvalidOperationException($"Sheet {Definition.Name} not found in game data");

        if (string.IsNullOrEmpty(referencedSheetNamespace))
            ReferencedSheetNamespace = null;
        else
        {
            if (referencedSheetNamespace!.EndsWith('.'))
                referencedSheetNamespace = referencedSheetNamespace![..^1];
            ReferencedSheetNamespace = referencedSheetNamespace;
        }

        SheetName = GameSheet.Name;
        ColumnHash = GameSheet.HeaderFile.GetColumnsHash();

        var orderedColumns = GameSheet.Columns.GroupBy(c => c.Offset).OrderBy(c => c.Key).SelectMany(g => g.OrderBy(c => c.Type)).ToList();

        (ParseCode, DefinitionCode) = ParseFields(Definition.Fields, Definition.Relations, orderedColumns, 0, out var offset, out _, fieldPrefix: useThis ? "this" : string.Empty, isRoot: true);

        if (offset != orderedColumns.Count)
            throw new InvalidOperationException($"Expected {orderedColumns.Count} columns, but only parsed {offset}");
    }

    private class RelationInfo
    {
        public string Name { get; }
        public List<string> FieldNames { get; }
        public IndentedStringBuilder DefinitionBuilder { get; }
        public int? ArraySize { get; private set; }
        public string RelationType => $"{Name}Struct";
        private bool PushedProperty { get; set; }

        public RelationInfo(string indentString, KeyValuePair<string, List<string>> relation)
        {
            Name = relation.Key;
            FieldNames = relation.Value;
            DefinitionBuilder = new IndentedStringBuilder(indentString);
        }

        public void MarkArraySize(IndentedStringBuilder pb, string fieldPrefix, string classPrefix, int size)
        {
            if (ArraySize.HasValue && ArraySize.Value != size)
                throw new InvalidOperationException("Related array size mismatch");
            if (!ArraySize.HasValue)
            {
                ArraySize = size;
                var name = ApplyPrefix(fieldPrefix, Name);
                pb.AppendLine($"{name} = new {classPrefix}{RelationType}[{size}];");
                pb.AppendLine($"for (var idx = 0; idx < {size}; idx++)");
                pb.AppendLine($"{name}[idx] = new();", 1);
            }
        }

        public void PushProperty(IndentedStringBuilder db)
        {
            if (!PushedProperty)
            {
                db.AppendLine($"public {RelationType}[] {Name} {{ get; internal set; }}");
                PushedProperty = true;
            }
        }
    }

    // TODO: field prefix check if empty before suffixing a .
    private (string ParseCode, string DefinitionCode) ParseFields(IEnumerable<Field> fields, IEnumerable<KeyValuePair<string, List<string>>>? relations, IReadOnlyList<ExcelColumnDefinition> columns, int columnIdxOffset, out int finalColumnIdxOffset, out string? wrappedType, char iterVariable = 'i', string offsetPrefix = "", string classPrefix = "", string fieldPrefix = "", bool isRoot = false)
    {
        if (iterVariable < 'a' || iterVariable > 'z')
            throw new InvalidOperationException("Iter variable must be a lowercase letter");

        relations ??= [];

        var pbBase = new IndentedStringBuilder(IndentString);

        var dbBase = new IndentedStringBuilder(IndentString);

        var relationDefs = relations.ToDictionary(r => r.Key, r => new RelationInfo(IndentString, r));

        var parseSnippets = new Dictionary<Field, string>();

        wrappedType = null;
        var byteOffset = 0;
        foreach (var field in fields)
        {
            var hasName = !string.IsNullOrEmpty(field.Name);
            var prefixedName = !hasName ? TrimPrefix(fieldPrefix) : ApplyPrefix(fieldPrefix, field.Name!);
            var prefixedOffsetPrefix = string.IsNullOrEmpty(offsetPrefix) ? "" : $"{offsetPrefix} + ";

            var db = dbBase;
            var relationName = (hasName ? relations.FirstOrDefault(r => r.Value.Contains(field.Name!)) : default).Key;
            var isRelation = !string.IsNullOrEmpty(relationName);
            if (isRelation)
            {
                var relDef = relationDefs[relationName];
                db = relDef.DefinitionBuilder;
                prefixedName = ApplyPrefix(fieldPrefix, relationName);

                if (field.Type != FieldType.Array)
                    throw new InvalidOperationException("Relation field must be an array");
                relDef.MarkArraySize(pbBase, fieldPrefix, classPrefix, field.Count ?? 1);
            }

            var pb = new IndentedStringBuilder(IndentString);

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
                        pb.AppendLine($"{prefixedName} = parser.ReadOffset<{typeName}>({prefixedOffsetPrefix}{byteOffset}, {Globalize<ExcelColumnDataType>()}.{column.Type});");
                        fieldTypeName = typeName;
                        columnIdxOffset++;
                        byteOffset += colSize;
                    }
                    break;
                case FieldType.Link:
                    {
                        var column = columns[columnIdxOffset];
                        var colSize = GetColumnSize(columns, columnIdxOffset);
                        var fieldRow = $"(uint)parser.ReadOffset<{LookupTypeName(column.Type)}>({prefixedOffsetPrefix}{byteOffset}, {Globalize<ExcelColumnDataType>()}.{column.Type})";
                        if (field.Targets == null)
                        {
                            if (field.Condition == null)
                                throw new InvalidOperationException($"Field {field.Name} has no targets or condition");

                            pb.AppendLine($"{prefixedName} = this.{field.Condition.Switch} switch");
                            pb.AppendLine("{");
                            using (pb.IndentScope())
                            {
                                foreach (var (val, targets) in field.Condition.Cases!)
                                {
                                    if (targets.Count == 1)
                                        pb.AppendLine($"{val} => new {Globalize("Lumina.Excel.LazyRow")}<{DecorateReferencedType(targets[0])}>(gameData, {fieldRow}, language),");
                                    else
                                        pb.AppendLine($"{val} => {Globalize<EmptyLazyRow>()}.GetFirstLazyRowOrEmpty(gameData, {fieldRow}, language, {string.Join(", ", targets.Select(GeneratorUtils.EscapeStringToken))}),");
                                }
                                pb.AppendLine($"_ => new {Globalize<EmptyLazyRow>()}({fieldRow}),");
                            }
                            pb.AppendLine("};");

                            fieldTypeName = Globalize<ILazyRow>();
                        }
                        else
                        {
                            if (field.Targets.Count == 1)
                            {
                                fieldTypeName = $"{Globalize("Lumina.Excel.LazyRow")}<{DecorateReferencedType(field.Targets[0])}>";
                                pb.AppendLine($"{prefixedName} = new {fieldTypeName}(gameData, {fieldRow}, language);");
                            }
                            else
                            {
                                fieldTypeName = Globalize<ILazyRow>();
                                pb.AppendLine($"{prefixedName} = {Globalize<EmptyLazyRow>()}.GetFirstLazyRowOrEmpty(gameData, {fieldRow}, language, {string.Join(", ", field.Targets.Select(GeneratorUtils.EscapeStringToken))});");
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
                        var (fieldParseCode, fieldDefCode) = ParseFields(subfields, field.Relations, columns, columnIdxOffset, out _, out var fieldWrappedType, (char)(iterVariable + 1), $"{prefixedOffsetPrefix}{byteOffset} + {iterVariable} * {size.byteSize}", subclassPrefix, subfieldPrefix);

                        if (string.IsNullOrEmpty(fieldWrappedType) == string.IsNullOrEmpty(fieldDefCode))
                            throw new InvalidOperationException("Array field must have either all named or one unnamed field");

                        var isSubtype = string.IsNullOrEmpty(fieldWrappedType);
                        if (isSubtype)
                        {
                            if (string.IsNullOrEmpty(field.Name))
                                throw new InvalidOperationException("Array field must have a name attached");
                            var tb = new IndentedStringBuilder(IndentString);
                            tb.AppendLine($"public class {field.Name}Struct");
                            tb.AppendLine("{");
                            tb.AppendLines(fieldDefCode, 1);
                            tb.AppendLine("}");
                            db.AppendLines(tb.ToString());
                            fieldWrappedType = $"{classPrefix}{field.Name}Struct";
                        }

                        if (!isRelation)
                            fieldTypeName = $"{fieldWrappedType!}[]";
                        else
                            fieldTypeName = fieldWrappedType!;

                        if (!isRelation)
                            pb.AppendLine($"{prefixedName} = new {fieldWrappedType}[{fieldCount}];");
                        pb.AppendLine($"for (var {iterVariable} = 0; {iterVariable} < {fieldCount}; {iterVariable}++)");
                        var hasNew = !isRelation && isSubtype;
                        var hasBraces = (hasNew ? 1 : 0) + fieldParseCode.Count(c => c == '\n') > 1;
                        if (hasBraces) pb.AppendLine("{");
                        using (pb.IndentScope())
                        {
                            if (hasNew)
                                pb.AppendLine($"{prefixedName}[{iterVariable}] = new();");
                            pb.AppendLines(fieldParseCode);
                        }
                        if (hasBraces) pb.AppendLine("}");

                        columnIdxOffset += size.columnCount * fieldCount;
                        byteOffset += size.byteSize * fieldCount;
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown field type {field.Type}");
            }

            parseSnippets.Add(field, pb.ToString());

            if (hasName)
                db.AppendLine($"public {fieldTypeName} {field.Name} {{ get; internal set; }}");
            else
                wrappedType = fieldTypeName;

            if (isRelation)
                relationDefs[relationName].PushProperty(dbBase);
        }

        // ordering is for parsing conditional links in the right order
        var orderedParseFields = isRoot ? OrderFieldDependencies(fields) : fields;

        foreach(var field in orderedParseFields)
            pbBase.AppendLines(parseSnippets[field]);

        foreach(var relDef in relationDefs.Values)
        {
            var tb = new IndentedStringBuilder(IndentString);
            tb.AppendLine($"public class {relDef.RelationType}");
            tb.AppendLine("{");
            tb.AppendLines(relDef.DefinitionBuilder.ToString(), 1);
            tb.AppendLine("}");
            dbBase.AppendLines(tb.ToString());
        }

        finalColumnIdxOffset = columnIdxOffset;
        return (pbBase.ToString(), dbBase.ToString());
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

    private string Globalize(string typeName) =>
        TypeGlobalizer.GlobalizeType(typeName);

    private string Globalize<T>() =>
        TypeGlobalizer.GlobalizeType(typeof(T).FullName);

    private string DecorateReferencedType(string typeName) =>
        ReferencedSheetNamespace == null ? typeName : Globalize($"{ReferencedSheetNamespace}.{typeName}");

    private static string ApplyPrefix(string prefix, string value)
    {
        if (string.IsNullOrEmpty(prefix))
            return value;
        if (prefix[^1] == '.')
            return $"{prefix}{value}";
        return $"{prefix}.{value}";
    }

    private static string TrimPrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return prefix;
        if (prefix[^1] == '.')
            return prefix[..^1];
        return prefix;
    }

    private string LookupTypeName(ExcelColumnDataType type) =>
        type switch
    {
        ExcelColumnDataType.String => Globalize<SeString>(),
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
}
