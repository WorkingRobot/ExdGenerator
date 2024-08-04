using System.Collections;

namespace ExdAccessor;

public readonly struct StructCollection<T>(Page page, uint offset, int size) : IReadOnlyList<T>
{
    private static readonly Func<Page, uint, uint, T> Constructor = (page, offset, i) => (T)Activator.CreateInstance(typeof(T), [page, offset, i])!;

    public T this[int index] => Constructor(page, offset, (uint)index);

    public int Count => size;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < size; ++i)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
