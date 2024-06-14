using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace ExdGenerator;

internal static class SourceConstants
{
    public const string GeneratedNamespace = "ExdGenerator.Generated";

    public static SourceText CreateAttributeSource(string attributeName) => SourceText.From($@"
using System;
namespace {GeneratedNamespace}
{{
    [global::System.CodeDom.Compiler.GeneratedCode(""ExdGenerator"", ""1.0.0"")]
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class {attributeName}Attribute : Attribute
    {{
        public string SchemaPath {{ get; }}
    
        public {attributeName}Attribute(string schemaPath)
        {{
            SchemaPath = schemaPath;
        }}
    }}
}}
", Encoding.UTF8);

    public static SourceText CreateSchemaSource(string? targetNamespace, string className, bool isPartial, SchemaSourceConverter converter) => SourceText.From($@"
{(string.IsNullOrEmpty(targetNamespace) ? string.Empty : $@"namespace {targetNamespace}
{{")}
    [global::System.CodeDom.Compiler.GeneratedCode(""ExdGenerator"", ""1.0.0"")]
    [global::Lumina.Excel.Sheet({GeneratorUtils.EscapeStringToken(converter.SheetName)}, 0x{converter.ColumnHash:X8})]
    {(isPartial ? "partial" : "public")} class {className} : global::Lumina.Excel.ExcelRow
    {{
{converter.DefinitionCode}

        public override void PopulateData(global::Lumina.Excel.RowParser parser, global::Lumina.GameData gameData, global::Lumina.Data.Language language)
        {{
            base.PopulateData(parser, gameData, language);

{converter.ParseCode}
        }}
    }}
{(string.IsNullOrEmpty(targetNamespace) ? string.Empty : "}")}
", Encoding.UTF8);
}
