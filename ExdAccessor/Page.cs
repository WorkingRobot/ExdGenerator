using Lumina.Text.ReadOnly;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExdAccessor;

public sealed class Page
{
    internal Module Module { get; }

    private readonly byte[] data;
    internal ReadOnlySpan<byte> Data => data;

    internal Page(Module module, byte[] pageData)
    {
        Module = module;
        data = pageData;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private D Read<D>(nuint offset) where D : struct =>
        Unsafe.As<byte, D>(ref Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(Data), offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySeString ReadString(nuint offset)
    {
        offset = ReadUInt32(offset);
        var stringLength = Data[(int)offset..].IndexOf((byte)0);
        return new ReadOnlySeString(data.AsMemory((int)offset, stringLength));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBool(nuint offset) =>
        Read<bool>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadInt8(nuint offset) =>
        Read<sbyte>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadUInt8(nuint offset) =>
        Read<byte>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16(nuint offset) =>
        Read<short>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16(nuint offset) =>
        Read<ushort>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32(nuint offset) =>
        Read<int>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32(nuint offset) =>
        Read<uint>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat32(nuint offset) =>
        Read<float>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64(nuint offset) =>
        Read<long>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64(nuint offset) =>
        Read<ulong>(offset);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadPackedBool(nuint offset, byte bit) =>
        (Read<byte>(offset) & (1 << bit)) != 0;
}
