using Lumina.Text.ReadOnly;

namespace ExdAccessor;

[Sheet("BaseParam", 0x8568AFE3)]
public readonly struct BaseParam(Page page, uint row, uint offset)
{
    public uint RowId => row;

    public ReadOnlySeString Name => page.ReadString(offset + 0);
    public ReadOnlySeString Description => page.ReadString(offset + 4);
    // ...
}

[Sheet("ItemFood", 0xE09A474D)]
public readonly struct ItemFood(Page page, uint row, uint offset)
{
    public uint RowId => row;

    public StructCollection<ParamsStruct> Params => new(page, offset, 3);
    public byte EXPBonusPercent => page.ReadUInt8(offset + 12);

    public readonly struct ParamsStruct(Page page, uint offset, uint i)
    {
        public short Max => page.ReadInt16(offset + 0 + i * 2 + 0);
        public short MaxHQ => page.ReadInt16(offset + 6 + i * 2 + 0);
        public LazyRow<BaseParam> BaseParam => new(page.Module, (uint)page.ReadInt16(offset + 0 + i * 2 + 0));
        public sbyte Value => page.ReadInt8(offset + 16 + i * 1 + 0);
        public sbyte ValueHQ => page.ReadInt8(offset + 19 + i * 1 + 0);
        public bool IsRelative => page.ReadBool(offset + 22 + i * 1 + 0);
    }
}
