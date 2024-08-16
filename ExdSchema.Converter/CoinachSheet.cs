using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExdSchema.Converter.Coinach;

internal class CoinachSheet
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CoinachSheet FromFile(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return FromStream(stream);
    }

    public static CoinachSheet FromStream(Stream stream)
    {
        return JsonSerializer.Deserialize<CoinachSheet>(stream, JsonOptions) ?? throw new ArgumentException("Invalid stream", nameof(stream));
    }

    public required string Sheet { get; init; }

    public string? DefaultColumn { get; init; }

    public bool? IsGenericReferenceTarget { get; init; }

    public required Definition[] Definitions { get; init; }
}

internal class Definition
{
    public string? Type { get; init; }

    public int? Index { get; init; }

    // single
    public string? Name { get; init; }
    public Converter? Converter { get; init; }

    // group
    public Definition[]? Members { get; init; }

    // repeat
    public int? Count { get; init; }
    [JsonPropertyName("definition")]
    public Definition? Subdefinition { get; init; }

    [JsonIgnore]
    public bool IsSingle => Type == null;

    [JsonIgnore]
    public bool IsGroup => string.Equals(Type, "group", StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsRepeat => string.Equals(Type, "repeat", StringComparison.Ordinal);
}

[JsonDerivedType(typeof(ColorConverter), "color")]
[JsonDerivedType(typeof(GenericConverter), "generic")]
[JsonDerivedType(typeof(IconConverter), "icon")]
[JsonDerivedType(typeof(MultirefConverter), "multiref")]
[JsonDerivedType(typeof(LinkConverter), "link")]
[JsonDerivedType(typeof(TomestoneConverter), "tomestone")]
[JsonDerivedType(typeof(ComplexLinkConverter), "complexlink")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
internal class Converter
{

}

internal class ColorConverter : Converter
{

}

internal class GenericConverter : Converter
{

}

internal class IconConverter : Converter
{

}

internal class MultirefConverter : Converter
{
    public required string[] Targets { get; init; }
}

internal class LinkConverter : Converter
{
    public required string Target { get; init; }
}

internal class TomestoneConverter : Converter
{

}

internal class ComplexLinkConverter : Converter
{
    public required SheetLinkData[] Links { get; init; }
}

internal class SheetLinkData
{
    public string? Sheet { get; init; }

    public string[]? Sheets { get; init; }

    // Go to the resolved sheet, and return the row referenced by column with name Project
    // If Key exists, resolve Key first, and then follow the Project column indirection.
    public string? Project { get; init; }
    
    // In the sheet, return the row where the column with name Key has the value.
    public string? Key { get; init; }

    public WhenCondition? When { get; init; }
}

internal class WhenCondition
{
    public required string Key { get; init; }

    public required int Value { get; init; }
}
