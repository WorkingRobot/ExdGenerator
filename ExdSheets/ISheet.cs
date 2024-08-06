using Lumina.Data;
using System.Collections;

namespace ExdSheets;

internal interface ISheet : IEnumerable
{
    Module Module { get; }
    Language Language { get; }

    bool HasRow(uint rowId);
    bool HasRow(uint rowId, ushort subRowId);
    ushort GetSubrowCount(uint rowId);
}
