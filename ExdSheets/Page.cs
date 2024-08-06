using Lumina.Text.ReadOnly;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ExdSheets;

public sealed class Page
{
    public Module Module { get; }

    private readonly byte[] data;
    private ReadOnlyMemory<byte> Data => data;

    private readonly ushort dataOffset;

    internal Page(Module module, byte[] pageData, ushort headerDataOffset)
    {
        Module = module;
        data = pageData;
        dataOffset = headerDataOffset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private D Read<D>(nuint offset) where D : unmanaged =>
        Unsafe.As<byte, D>(ref Unsafe.AddByteOffset(ref MemoryMarshal.GetArrayDataReference(data), offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static float ReverseEndianness(float v) =>
        Unsafe.BitCast<uint, float>(BinaryPrimitives.ReverseEndianness(Unsafe.BitCast<float, uint>(v)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySeString ReadString(nuint offset, nuint structOffset)
    {
        offset = ReadUInt32(offset) + structOffset + dataOffset;
        var data = Data[(int)offset..];
        var stringLength = data.Span.IndexOf((byte)0);
        return new ReadOnlySeString(data[..stringLength]);
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
        BinaryPrimitives.ReverseEndianness(Read<short>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16(nuint offset) =>
        BinaryPrimitives.ReverseEndianness(Read<ushort>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32(nuint offset) =>
        BinaryPrimitives.ReverseEndianness(Read<int>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32(nuint offset) =>
        BinaryPrimitives.ReverseEndianness(Read<uint>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadFloat32(nuint offset) =>
        ReverseEndianness(Read<float>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64(nuint offset) =>
        BinaryPrimitives.ReverseEndianness(Read<long>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64(nuint offset) =>
        BinaryPrimitives.ReverseEndianness(Read<ulong>(offset));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadPackedBool(nuint offset, byte bit) =>
        (Read<byte>(offset) & (1 << bit)) != 0;
}
