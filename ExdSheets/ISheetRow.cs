namespace ExdSheets;

public interface ISheetRow<T> where T : struct
{
    abstract static T Create(Page page, uint offset, uint row);

    abstract static T Create(Page page, uint offset, uint row, ushort subrow);
}
