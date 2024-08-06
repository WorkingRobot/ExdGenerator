using System.Diagnostics.CodeAnalysis;

namespace ExdSheets;

public abstract class LazyRow
{
    public uint Row { get; protected init; }

    public abstract bool IsValueCreated { get; }
    public abstract bool HasValidValue { get; }

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
    private bool attemptedValueCreation;
    private T? value;

    public override bool IsValueCreated => attemptedValueCreation;
    public override bool HasValidValue => ValueNullable.HasValue;

    [MemberNotNull(nameof(ValueNullable))]
    public T Value => ValueNullable ?? throw new NullReferenceException();

    public T? ValueNullable
    {
        get
        {
            if (!attemptedValueCreation)
            {
                attemptedValueCreation = true;
                value = module.GetSheet<T>().TryGetRow(Row);
            }
            return value;
        }
    }

    public LazyRow(Module module, uint rowId)
    {
        this.module = module;
        Row = rowId;
    }
}

public sealed class LazyRowEmpty : LazyRow
{
    public override bool IsValueCreated => false;
    public override bool HasValidValue => false;

    public LazyRowEmpty(uint rowId)
    {
        Row = rowId;
    }
}
