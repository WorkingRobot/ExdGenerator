using Lumina.Data;
using Lumina.Data.Files.Excel;
using Lumina.Data.Structs.Excel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ExdAccessor;

public sealed class Sheet<T> where T : struct
{
    public Module Module { get; }

    public Language Language { get; }

    private T[]? Rows { get; }
    private T[][]? Subrows { get; }

    [MemberNotNullWhen(true, nameof(Subrows))]
    [MemberNotNullWhen(false, nameof(Rows))]
    public bool HasSubrows { get; }

    private static readonly Func<Page, uint, uint, T> RowConstructor = (page, offset, row) => (T)Activator.CreateInstance(typeof(T), [page, offset, row])!;
    private static readonly Func<Page, uint, uint, uint, T> SubRowConstructor = (page, offset, row, subrow) => (T)Activator.CreateInstance(typeof(T), [page, offset, row, subrow])!;
    private static SheetAttribute Attribute => typeof(T).GetCustomAttribute<SheetAttribute>() ?? throw new InvalidOperationException("T has no SheetAttribute. Use the explicit sheet constructor.");

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

        if (HasSubrows)
            Subrows = new T[(int)headerFile.Header.RowCount][];
        else
            Rows = new T[(int)headerFile.Header.RowCount];

        foreach (var pageDef in headerFile.DataPages)
        {
            var filePath = Language == Language.None ? $"exd/{sheetName}_{pageDef.StartId}.exd" : $"exd/{sheetName}_{pageDef.StartId}_{LanguageUtil.GetLanguageStr(Language)}.exd";
            var fileData = module.GameData.GetFile<ExcelDataFile>(filePath);
            if (fileData == null)
                continue;

            var page = new Page(Module, fileData.Data);

            foreach (var rowPtr in fileData.RowData.Values)
            {
                fileData.Reader.BaseStream.Position = rowPtr.Offset;
                var rowHeader = ExcelDataRowHeader.Read(fileData.Reader);
                var rowOffset = rowPtr.Offset + 6;

                if (HasSubrows)
                {
                    var rows = new T[rowHeader.RowCount];
                    for (var i = 0u; i < rowHeader.RowCount; ++i)
                    {
                        var subRowOffset = rowOffset + 2 + i * (headerFile.Header.DataOffset + 2u);
                        rows[i] = SubRowConstructor(page, rowPtr.RowId, i, subRowOffset);
                    }
                    Subrows![rowPtr.RowId] = rows;
                }
                else
                    Rows![rowPtr.RowId] = RowConstructor(page, rowPtr.RowId, rowOffset);
            }
        }
    }
}
