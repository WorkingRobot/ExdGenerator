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

    public static SourceText CreateSchemaSource(string? targetNamespace, string className, bool isPublic, SchemaSourceConverter converter) => SourceText.From($@"
{(string.IsNullOrEmpty(targetNamespace) ? string.Empty : $@"namespace {targetNamespace}
{{")}
    {(!isPublic ? string.Empty : "public ")}partial class {className} : global::Lumina.Excel.ExcelRow
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
