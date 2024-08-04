namespace ExdAccessor;

public abstract class LazyRow
{
    public uint Row { get; protected init; }

    public abstract bool IsValueCreated { get; }
}

public sealed class LazyRow<T> : LazyRow where T : struct
{
    private readonly Module module;
    private T? value;

    public override bool IsValueCreated => value.HasValue;

    public T Value => value ??= module.GetSheet<T>().GetRow(RowId);

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
