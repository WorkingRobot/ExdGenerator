using System.Collections;
using System.Runtime.CompilerServices;

namespace ExdSheets;

public readonly struct LazyCollection<T>(Page page, uint parentOffset, uint offset, Func<Page, uint, uint, uint, T> ctor, int size) : IReadOnlyList<T>
{
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, size);
            return ctor(page, parentOffset, offset, (uint)index);
        }
    }

    public int Count => size;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < size; ++i)
            yield return this[i];
    }

    IEnumerator IEnumerable.GetEnumerator() =>
        GetEnumerator();
}
