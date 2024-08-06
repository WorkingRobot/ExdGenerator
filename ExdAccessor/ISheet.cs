using Lumina.Data;

namespace ExdAccessor;

internal interface ISheet
{
    Module Module { get; }
    Language Language { get; }

    bool HasRow(uint rowId);
    bool HasRow(uint rowId, uint subRowId);
    ushort GetSubrowCount(uint rowId);
}
