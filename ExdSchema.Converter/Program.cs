using DotNext.Collections.Generic;
using ExdGenerator.Schema.Exd;
using ExdSchema.Converter.Coinach;
using Lumina;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;

namespace ExdSchema.Converter;

internal class Program
{
    static List<string> GenericTargetSheets = [];

    private static void Main(string[] args)
    {
        var gameData = new GameData(@"J:\Programs\steam\steamapps\common\FINAL FANTASY XIV Online\game\sqpack", new() { LoadMultithreaded = true });

        List<ExdSheet> newSheets = [];

        foreach (var file in Directory.EnumerateFiles(@"J:\Code\Projects\SaintCoinach\SaintCoinach\Definitions"))
        {
            if (!file.EndsWith(".json", StringComparison.Ordinal))
                continue;

            Console.WriteLine($"Deserializing {file}");
            var sheet = CoinachSheet.FromFile(file);

            var newSheet = new ExdSheet()
            {
                Name = sheet.Sheet,
                DisplayField = sheet.DefaultColumn,
                Fields = null!
            };

            if (sheet.IsGenericReferenceTarget ?? false)
                GenericTargetSheets.Add(sheet.Sheet);

            var header = gameData.GetFile<ExcelHeaderFile>($"exd/{sheet.Sheet}.exh");
            if (header == null)
                throw new Exception($"Header for {sheet.Sheet} not found");

            var defs = ResolveAllDefinitions(sheet.Definitions);

            if (sheet.Sheet == "BattleLeve")
                Console.WriteLine("Converting");

            var f = ConvertFields(defs, null);

            List<int> unkCols = [];
            for (var i = 0; i < header.ColumnDefinitions.Length; ++i)
            {
                var found = false;
                foreach (var range in f.ColumnIndexes.Values)
                {
                    if (range.Start.Value <= i && i < range.End.Value)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Console.WriteLine($"WARN: Column {i} not found in definitions");
                    unkCols.Add(i);
                }
            }
            unkCols = [.. unkCols.OrderBy(
                f => GetOffset(header, f),
                Comparer<(ushort, byte?)>.Create((a, b) =>
                {
                    var cmp = a.Item1.CompareTo(b.Item1);
                    if (cmp != 0)
                        return cmp;
                    return a.Item2!.Value.CompareTo(b.Item2!.Value);
                })
            )];
            var unkColIdx = 0;
            foreach (var unkCol in unkCols)
            {
                var newField = new Field()
                {
                    Name = $"UnkColumn{unkColIdx++}",
                    Type = FieldType.Scalar
                };
                f.Fields.Add(newField);
                f.ColumnIndexes.Add(newField, unkCol..(unkCol + 1));
            }

            var (newFields, newRelations) = InvertFields(f.Fields, f.ColumnIndexes);
            newFields = OrderFields(newFields, f.ColumnIndexes, header);

            if (sheet.Sheet == "BaseParam")
                Console.WriteLine("Converted");
        }
    }

    private static List<Field> OrderFields(List<Field> fields, IReadOnlyDictionary<Field, Range> colIdxs, ExcelHeaderFile header)
    {
        return [.. fields.Order(
            Comparer<Field>.Create((a, b) =>
            {
                var aO = GetOffset(header, colIdxs[a].Start.Value);
                var bO = GetOffset(header, colIdxs[b].Start.Value);
                var cmp = aO.Offset.CompareTo(bO.Offset);
                if (cmp != 0)
                    return cmp;
                cmp = aO.BitOffset.HasValue.CompareTo(bO.BitOffset.HasValue);
                if (cmp != 0)
                    return 0;
                if (!aO.BitOffset.HasValue)
                    return 0;
                return aO.BitOffset.Value.CompareTo(bO.BitOffset!.Value);
            })
        )];
    }

    // scalar -> leave as is
    // array >1 item -> flatten and convert each field to an array, and add relations
    // other arrays -> add to relation candidates
    // resolve and add relations when needed at the end
    private static (List<Field> Fields, Dictionary<string, List<string>> Relations) InvertFields(List<Field> fields, Dictionary<Field, Range> colIdxs)
    {
        List<Field> newFields = [];
        Dictionary<string, List<string>> newRelations = [];

        foreach (var field in fields)
        {
            if (field.Type != FieldType.Array || (field.Fields?.Count ?? 1) == 1)
            {
                newFields.Add(field);
                continue;
            }

            var subfields = field.Fields!;
            (subfields, var subRelations) = InvertFields(subfields, colIdxs);
            foreach (var subfield in subfields)
            {
                var newField = new Field()
                {
                    Name = subfield.Name,
                    Type = FieldType.Array,
                    Count = field.Count,
                };
                if (subfield.Type != FieldType.Scalar)
                    newField.Fields = [subfield];
                newFields.Add(newField);
                colIdxs[newField] = colIdxs[subfield];
            }
            foreach (var (k, v) in subRelations)
                newRelations.Add(k, v);
            newRelations.Add(field.Name!, [.. subfields.Select(s => s.Name)]);
        }

        // swap array sizes
        foreach (var field in newFields)
            ShiftField(field);

        // add group resolution
        Dictionary<int, List<Field>> sizedFields = [];
        foreach (var field in newFields)
        {
            if (field.Type == FieldType.Array && (field.Fields?.Count ?? 1) == 1)
                sizedFields.GetOrAdd(field.Count!.Value, []).Add(field);
        }

        //foreach (var (size, groupedFields) in sizedFields)
        //{
        //    if (groupedFields.Count > 1)
        //    {
        //        foreach (var field in groupedFields)
        //            newFields.Remove(field);

        //        var newField = new Field()
        //        {
        //            Name = $"Sized{size}Group",
        //            Type = FieldType.Array,
        //            Count = size
        //        };
        //    }
        //}

        return (newFields, newRelations);
    }

    private static void ShiftField(Field field)
    {
        var fields = new List<Field>();

        while (field.Type != FieldType.Array)
        {
            fields.Add(field);
            if (field.Fields?.Count != 1)
                break;
            field = field.Fields[0];
        }

        if (fields.Count > 1)
        {
            var lastSize = fields[^1].Count;

            for (var i = fields.Count - 1; i > 0; --i)
                fields[i].Count = fields[i - 1].Count;

            fields[0].Count = lastSize;
        }
    }
    // repeat -> repeat -> ...
    // this means to reverse the repeat order when converting

    // repeat -> group
    // this is a array w relation

    // repeat -> single
    // plain array w 1 field

    private static (List<Field> Fields, Dictionary<Field, Range> ColumnIndexes, int ColumnCount) ConvertFields(Definition[] definitions, int? columnOffset)
    {
        var fields = new List<Field>();
        var colIdxs = new Dictionary<Field, Range>();
        var unkFieldIdx = 0;
        var currentOffset = 0;

        foreach (var def in definitions)
        {
            int colIdx;
            if (!columnOffset.HasValue)
                colIdx = def.Index ?? 0;
            else
                colIdx = columnOffset.Value + currentOffset;
            if (def.IsSingle)
            {
                var newField = new Field()
                {
                    Name = def.Name!,
                    Type = FieldType.Scalar
                };
                if (def.Converter != null)
                    PopulateFieldWithConverter(newField, def.Converter);
                fields.Add(newField);
                colIdxs.Add(newField, colIdx..(colIdx + 1));
                currentOffset++;
            }
            else if (def.IsRepeat)
            {
                var subdef = def.Subdefinition!;
                if (subdef.IsGroup)
                {
                    var groupSubdefs = ResolveAllDefinitions(subdef.Members!);
                    if (groupSubdefs.Length == 1)
                        subdef = groupSubdefs[0];
                    else
                    {
                        var newFields = ConvertFields(groupSubdefs, 0);
                        foreach (var (k,v) in newFields.ColumnIndexes)
                            colIdxs.Add(k, (colIdx + v.Start.Value)..(colIdx + v.End.Value));

                        var newField = new Field()
                        {
                            Type = FieldType.Array,
                            Count = def.Count,
                            Name = $"UnknownRepeat{unkFieldIdx++}",
                            Fields = newFields.Fields
                        };
                        fields.Add(newField);
                        colIdxs.Add(newField, colIdx..(colIdx + newFields.ColumnCount * def.Count!.Value));
                        currentOffset += newFields.ColumnCount * def.Count!.Value;
                    }
                }
                if (subdef.IsSingle)
                {
                    var newField = new Field()
                    {
                        Type = FieldType.Array,
                        Count = def.Count,
                        Name = subdef.Name!
                    };
                    if (subdef.Converter != null)
                    {
                        var subField = new Field();
                        PopulateFieldWithConverter(subField, subdef.Converter);
                        newField.Fields = [subField];
                        colIdxs.Add(subField, colIdx..(colIdx + 1));
                    }
                    fields.Add(newField);
                    colIdxs.Add(newField, colIdx..(colIdx + def.Count!.Value));
                    currentOffset += def.Count!.Value;
                }
                if (subdef.IsRepeat)
                {
                    // add as is; we can swap the order at the end
                    var newFields = ConvertFields([subdef], currentOffset);
                    foreach (var (k, v) in newFields.ColumnIndexes)
                        colIdxs.Add(k, v);

                    var newField = new Field()
                    {
                        Type = FieldType.Array,
                        Count = def.Count,
                        Name = $"UnknownRepeat{unkFieldIdx++}",
                        Fields = newFields.Fields
                    };
                    fields.Add(newField);
                    colIdxs.Add(newField, colIdx..(colIdx + newFields.ColumnCount * def.Count!.Value));
                    currentOffset += newFields.ColumnCount * def.Count!.Value;
                }
            }
            else if (def.IsGroup)
                throw new Exception("No groups are allowed after resolving definitions");
        }

        return (fields, colIdxs, currentOffset);
    }

    // repeat count 1 => ignore the repeat and flatten it
    // plain group => flatten it
    private static Definition[]? ResolveDefinitions(Definition[] definitions)
    {
        var newDefinitions = new List<Definition>();
        var isDirty = false;

        foreach (var def in definitions)
        {
            if (def.IsRepeat && def.Subdefinition!.IsSingle && def.Count == 1)
            {
                newDefinitions.Add(def.Subdefinition);
                isDirty = true;
            }
            else if (def.IsGroup)
            {
                newDefinitions.AddRange(def.Members!);
                isDirty = true;
            }
            else
                newDefinitions.Add(def);
        }

        if (isDirty)
            return [.. newDefinitions];
        return null;
    }

    private static Definition[] ResolveAllDefinitions(Definition[] definitions)
    {
        while (true)
        {
            var newDefs = ResolveDefinitions(definitions);
            if (newDefs == null)
                break;
            definitions = newDefs;
        }
        return definitions;
    }

    private static (ushort Offset, byte? BitOffset) GetOffset(ExcelHeaderFile header, int columnIdx)
    {
        var col = header.ColumnDefinitions[columnIdx];
        if (col.Type >= ExcelColumnDataType.PackedBool0)
            return (col.Offset, (byte)(col.Type - ExcelColumnDataType.PackedBool0));
        return (col.Offset, null);
    }

    private static void PopulateFieldWithConverter(Field field, Coinach.Converter converter)
    {
        var type = converter.GetType();
        if (converter is ColorConverter)
            field.Type = FieldType.Color;
        else if (converter is GenericConverter)
        {
            field.Type = FieldType.Link;
            field.Targets = GenericTargetSheets;
        }
        else if (converter is IconConverter)
            field.Type = FieldType.Icon;
        else if (converter is MultirefConverter multiRef)
        {
            field.Type = FieldType.Link;
            field.Targets = [.. multiRef.Targets];
        }
        else if (converter is LinkConverter link)
        {
            field.Type = FieldType.Link;
            field.Targets = [link.Target];
        }
        else if (converter is TomestoneConverter)
        {
            field.Type = FieldType.Link;
            field.Targets = ["Tomestones", "Item"];
        }
        else if (converter is ComplexLinkConverter complexLink)
        {

            field.Type = FieldType.Link;
            var isConditional = complexLink.Links.Any(l => l.When != null);
            if (isConditional)
            {
                if (complexLink.Links.Any(l => l.When == null))
                    throw new Exception("Cannot mix conditional and unconditional links");

                field.Condition = new();

                var columnName = complexLink.Links[0].When!.Key;
                if (complexLink.Links.Any(l => !l.When!.Key.Equals(columnName, StringComparison.Ordinal)))
                    Console.WriteLine("WARN: Cannot mix different conditional columns");

                field.Condition.Switch = columnName;
                field.Condition.Cases = new(complexLink.Links.Length);
                foreach (var linkCase in complexLink.Links)
                {
                    var val = linkCase.When!.Value;
                    var sheets = linkCase.Sheets ?? [linkCase.Sheet!];
                    if (sheets.Length == 0 || sheets.Any(string.IsNullOrWhiteSpace))
                        throw new Exception("Sheets are invalid");
                    field.Condition.Cases.Add(val, [.. sheets]);
                }
            }
            else
            {
                List<string> sheets = [];
                foreach (var linkCase in complexLink.Links)
                {
                    var caseSheets = linkCase.Sheets ?? [linkCase.Sheet!];
                    if (caseSheets.Length == 0 || caseSheets.Any(string.IsNullOrWhiteSpace))
                        throw new Exception("Sheets are invalid");

                    sheets.AddRange(caseSheets);
                }
                field.Targets = [.. sheets.Distinct()];
            }
        }
        else
            throw new Exception($"Unknown converter type {type}");
    }
}
