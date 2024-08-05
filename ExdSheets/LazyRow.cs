namespace ExdAccessor;

public abstract class LazyRow
{
    public uint Row { get; protected init; }

    public abstract bool IsValueCreated { get; }

    internal LazyRow()
    {

    }

    public static LazyRow GetFirstValidRowOrEmpty(Module module, uint rowId, params Type[] sheetTypes)
    {
        foreach (var sheetType in sheetTypes)
        {
            if (module.GetSheetGeneric(sheetType) is { } sheet)
            {
                if (sheet.HasRow(rowId))
                    return (LazyRow)Activator.CreateInstance(typeof(LazyRow<>).MakeGenericType(sheetType), module, rowId)!;
            }
        }

        return new LazyRowEmpty(rowId);
    }
}

public sealed class LazyRow<T> : LazyRow where T : struct
{
    private readonly Module module;
    private T? value;

    public override bool IsValueCreated => value.HasValue;

    public T Value => value ??= module.GetSheet<T>().GetRow(Row);

    public LazyRow(Module module, uint rowId)
    {
        this.module = module;
        Row = rowId;
    }
}

public sealed class LazyRowEmpty : LazyRow
{
    public override bool IsValueCreated => false;

    public LazyRowEmpty(uint rowId)
    {
        Row = rowId;
    }
}
