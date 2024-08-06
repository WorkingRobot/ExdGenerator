using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ExdSheets;

public sealed class Sheet<T> : ISheet, IReadOnlyList<T> where T : struct
{
    public Module Module { get; }

    public Language Language { get; }

    private List<Page> Pages { get; }
    private FrozenDictionary<uint, (int PageIdx, uint Offset)>? Rows { get; }
    private FrozenDictionary<uint, (int PageIdx, uint Offset, ushort RowCount)>? Subrows { get; }
    private ushort SubrowDataOffset { get; }

    [MemberNotNullWhen(true, nameof(Subrows), nameof(SubrowDataOffset))]
    [MemberNotNullWhen(false, nameof(Rows))]
    public bool HasSubrows { get; }

    private static readonly Func<Page, uint, uint, T> RowConstructor = (page, offset, row) => (T)Activator.CreateInstance(typeof(T), [page, offset, row])!;
    private static readonly Func<Page, uint, uint, ushort, T> SubRowConstructor = (page, offset, row, subrow) => (T)Activator.CreateInstance(typeof(T), [page, offset, row, subrow])!;
    private static SheetAttribute Attribute => typeof(T).GetCustomAttribute<SheetAttribute>() ?? throw new InvalidOperationException("T has no SheetAttribute. Use the explicit sheet constructor.");

    public int Count => Rows?.Count ?? Subrows!.Count;

    public T this[int index] => GetRow((uint)index);

    public Sheet(Module module) : this(module, module.Language)
    {

    }

    public Sheet(Module module, Language requestedLanguage) : this(module, requestedLanguage, Attribute.Name, Attribute.ColumnHash)
    {
        
    }

    public Sheet(Module module, Language requestedLanguage, string sheetName, uint? columnHash = null)
    {
        Module = module;

        var headerFile = module.GameData.GetFile<ExcelHeaderFile>($"exd/{sheetName}.exh") ?? throw new ArgumentException("Invalid sheet name", nameof(sheetName));

        if (columnHash is { } hash && headerFile.GetColumnsHash() != hash)
            throw new ArgumentException("Column hash mismatch", nameof(columnHash));

        HasSubrows = headerFile.Header.Variant == ExcelVariant.Subrows;

        Language = headerFile.Languages.Contains(requestedLanguage) ? requestedLanguage : Language.None;

        Dictionary<uint, (int PageIdx, uint Offset)>? rows = null;
        Dictionary<uint, (int PageIdx, uint Offset, ushort RowCount)>? subrows = null;

        if (HasSubrows)
        {
            subrows = new((int)headerFile.Header.RowCount);
            SubrowDataOffset = headerFile.Header.DataOffset;
        }
        else
            rows = new((int)headerFile.Header.RowCount);

        Pages = new(headerFile.DataPages.Length);
        var pageIdx = 0;
        foreach (var pageDef in headerFile.DataPages)
        {
            var filePath = Language == Language.None ? $"exd/{sheetName}_{pageDef.StartId}.exd" : $"exd/{sheetName}_{pageDef.StartId}_{LanguageUtil.GetLanguageStr(Language)}.exd";
            var fileData = module.GameData.GetFile<ExcelDataFile>(filePath);
            if (fileData == null)
                continue;

            var newPage = new Page(Module, fileData.Data, headerFile.Header.DataOffset);
            Pages.Add(newPage);

            foreach (var rowPtr in fileData.RowData.Values)
            {
                var (rowDataSize, subrowCount) = (newPage.ReadUInt32(rowPtr.Offset), newPage.ReadUInt16(rowPtr.Offset + 4));
                var rowOffset = rowPtr.Offset + 6;

                if (HasSubrows)
                {
                    if (subrowCount > 0)
                        subrows!.Add(rowPtr.RowId, (pageIdx, rowOffset, subrowCount));
                }
                else
                    rows!.Add(rowPtr.RowId, (pageIdx, rowOffset));
            }

            pageIdx++;
        }

        if (HasSubrows)
            Subrows = subrows!.ToFrozenDictionary();
        else
            Rows = rows!.ToFrozenDictionary();
    }

    public bool HasRow(uint rowId)
    {
        if (HasSubrows)
            return Subrows.ContainsKey(rowId);

        return Rows.ContainsKey(rowId);
    }

    public bool HasRow(uint rowId, ushort subRowId)
    {
        if (!HasSubrows)
            throw new NotSupportedException("Cannot access subrow in a sheet that doesn't support any.");

        ref readonly var val = ref Subrows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            return false;

        return subRowId < val.RowCount;
    }

    public T? TryGetRow(uint rowId)
    {
        if (HasSubrows)
            return TryGetRow(rowId, 0);

        ref readonly var val = ref Rows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            return null;

        return RowConstructor(Pages[val.PageIdx], val.Offset, rowId);
    }

    public T? TryGetRow(uint rowId, ushort subRowId)
    {
        if (!HasSubrows)
            throw new NotSupportedException("Cannot access subrow in a sheet that doesn't support any.");

        ref readonly var val = ref Subrows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            return null;

        if (subRowId >= val.RowCount)
            return null;

        return SubRowConstructor(Pages[val.PageIdx], val.Offset + 2 + (subRowId * (SubrowDataOffset + 2u)), rowId, subRowId);
    }


    public T GetRow(uint rowId) =>
        TryGetRow(rowId) ??
            throw new ArgumentOutOfRangeException(nameof(rowId), "Row does not exist");

    public T GetRow(uint rowId, ushort subRowId)
    {
        if (!HasSubrows)
            throw new NotSupportedException("Cannot access subrow in a sheet that doesn't support any.");

        ref readonly var val = ref Subrows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            throw new ArgumentOutOfRangeException(nameof(rowId), "Row does not exist");

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(subRowId, val.RowCount);

        return SubRowConstructor(Pages[val.PageIdx], val.Offset + 2 + (subRowId * (SubrowDataOffset + 2u)), rowId, subRowId);
    }

    public ushort? TryGetSubrowCount(uint rowId)
    {
        if (!HasSubrows)
            return null;

        ref readonly var val = ref Subrows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            return null;

        return val.RowCount;
    }

    public ushort GetSubrowCount(uint rowId)
    {
        if (!HasSubrows)
            throw new NotSupportedException("Cannot access subrow in a sheet that doesn't support any.");

        ref readonly var val = ref Subrows.GetValueRefOrNullRef(rowId);
        if (Unsafe.IsNullRef(in val))
            throw new ArgumentOutOfRangeException(nameof(rowId), "Row does not exist");

        return val.RowCount;
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (!HasSubrows)
        {
            foreach (var rowData in Rows)
                yield return RowConstructor(Pages[rowData.Value.PageIdx], rowData.Value.Offset, rowData.Key);
        }
        else
        {
            foreach (var rowData in Subrows)
            {
                for (ushort i = 0; i < rowData.Value.RowCount; ++i)
                    yield return SubRowConstructor(Pages[rowData.Value.PageIdx], rowData.Value.Offset + 2 + (i * (SubrowDataOffset + 2u)), rowData.Key, i);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
