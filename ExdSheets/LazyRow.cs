namespace ExdSheets;

public readonly struct LazyRow(Module? module, uint rowId, Type? rowType)
{
    public uint RowId => rowId;

    public bool IsUntyped => rowType == null;

    public bool Is<T>() where T : struct, ISheetRow<T> =>
        typeof(T) == rowType;

    public T? TryGetValue<T>() where T : struct, ISheetRow<T>
    {
        if (!Is<T>())
            return null;

        return module!.GetSheet<T>().TryGetRow(RowId);
    }

    public static LazyRow GetFirstValidRowOrUntyped(Module module, uint rowId, params Type[] sheetTypes)
    {
        foreach (var sheetType in sheetTypes)
        {
            if (module.GetSheetGeneric(sheetType) is { } sheet)
            {
                if (sheet.HasRow(rowId))
                    return new(module, rowId, sheetType);
            }
        }

        return CreateUntyped(rowId);
    }

    public static LazyRow Create<T>(Module module, uint rowId) where T : struct, ISheetRow<T> => new(module, rowId, typeof(T));

    public static LazyRow CreateUntyped(uint rowId) => new(null, rowId, null);
}

public readonly struct LazyRow<T>(Module module, uint rowId) where T : struct, ISheetRow<T>
{
    private readonly Sheet<T> sheet = module.GetSheet<T>();

    public uint RowId => rowId;

    public bool IsValid => sheet.HasRow(RowId);

    public T Value => sheet.GetRow(RowId);

    public T? ValueNullable => sheet.TryGetRow(RowId);

    private LazyRow ToGeneric() => LazyRow.Create<T>(module, rowId);

    public static explicit operator LazyRow(LazyRow<T> row) => row.ToGeneric();
}
