using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace ExdGenerator;

internal static class SourceConstants
{
    public const string GeneratedNamespace = "ExdGenerator.Generated";
    public const string Version = "1.0.0";

    public static SourceText CreateAttributeSource(string attributeName, bool useFileScopedNamespace)
    {
        var ret = $@"
[GeneratedCode(""ExdGenerator"", {GeneratorUtils.EscapeStringToken(Version)})]
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
internal sealed class {attributeName}Attribute : Attribute
{{
    public string SchemaPath {{ get; }}
    
    public {attributeName}Attribute(string schemaPath)
    {{
        SchemaPath = schemaPath;
    }}
}}";

        ret = ScopeNamespace(useFileScopedNamespace, "    ", GeneratedNamespace, ret);

        ret = $@"
using System;
using System.CodeDom.Compiler;
{ret}";

        return SourceText.From(ret.Trim(), Encoding.UTF8);
    }

    public static SourceText CreateSchemaSource(string? targetNamespace, string className, bool isPartial, bool useFileScopedNamespace, SchemaSourceConverter converter)
    {
        var globalize = converter.TypeGlobalizer.GlobalizeType;

        var sb = new IndentedStringBuilder(converter.IndentString);
        sb.AppendLine($@"[{globalize("System.CodeDom.Compiler.GeneratedCode")}(""ExdGenerator"", {GeneratorUtils.EscapeStringToken(Version)})]");
        sb.AppendLine($@"[{globalize("Lumina.Excel.Sheet")}({GeneratorUtils.EscapeStringToken(converter.SheetName)}, 0x{converter.ColumnHash:X8})]");
        sb.AppendLine($@"{(isPartial ? "partial" : "public")} class {className} : {globalize("Lumina.Excel.ExcelRow")}");
        sb.AppendLine("{");
        using (sb.IndentScope())
        {
            sb.AppendLines(converter.DefinitionCode);
            sb.AppendLine();
            sb.AppendLine($@"public override void PopulateData({globalize("Lumina.Excel.RowParser")} parser, {globalize("Lumina.GameData")} gameData, {globalize("Lumina.Data.Language")} language)");
            sb.AppendLine("{");
            using (sb.IndentScope())
            {
                sb.AppendLine("base.PopulateData(parser, gameData, language);");
                sb.AppendLine();
                sb.AppendLines(converter.ParseCode);
            }
            sb.AppendLine("}");
        }
        sb.AppendLine("}");

        var ret = sb.ToString();

        if (!string.IsNullOrEmpty(targetNamespace))
            ret = ScopeNamespace(useFileScopedNamespace, converter.IndentString, targetNamespace!, ret);

        ret = $"{converter.TypeGlobalizer.GetUsings()}\n{ret}";

        return SourceText.From(ret.Trim(), Encoding.UTF8);
    }

    private static string ScopeNamespace(bool useFileScope, string indentString, string ns, string text)
    {
        var b = new StringBuilder();
        text = text.Trim();
        if (useFileScope)
        {
            b.AppendLine($"namespace {ns};");
            b.AppendLine();
            b.Append(text);
        }
        else
        {
            b.AppendLine($"namespace {ns}");
            b.AppendLine("{");
            b.AppendLine(IndentedStringBuilder.Indent(text, indentString, 1));
            b.AppendLine("}");
        }
        return b.ToString();
    }
}
