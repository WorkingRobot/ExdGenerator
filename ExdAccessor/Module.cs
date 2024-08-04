using Lumina;
using Lumina.Data;
using System.Collections.Concurrent;

namespace ExdAccessor;

public sealed class Module(GameData gameData, Language requestedLanguage = Language.None)
{
    public GameData GameData { get; } = gameData;

    public Language Language { get; } = requestedLanguage;

    private ConcurrentDictionary<(Type sheetType, Language requestedLanguage), object> SheetCache { get; } = [];

    public Sheet<T> GetSheet<T>(Language? language = null) where T : struct
    {
        language ??= Language;

        return (Sheet<T>)SheetCache.GetOrAdd((typeof(T), language.Value), _ => new Sheet<T>(this, language.Value));
    }
}
