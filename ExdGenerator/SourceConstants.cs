using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace ExdGenerator;

internal static class SourceConstants
{
    public const string GeneratedNamespace = "ExdGenerator.Generated";
    public const string Version = "2.0.0";

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
        sb.AppendLine($@"readonly {(isPartial ? "partial" : "public")} struct {className}({globalize("Lumina.Excel.ExcelPage")} page, uint offset, uint row{(converter.HasSubrows ? ", ushort subrow" : string.Empty)}) : {globalize("Lumina.Excel.IExcelRow")}<{className}>");
        sb.AppendLine("{");
        using (sb.IndentScope())
        {
            sb.AppendLine("public uint RowId => row;");
            if (converter.HasSubrows)
                sb.AppendLine("public ushort SubrowId => subrow;");
            sb.AppendLine();
            sb.AppendLines(converter.Code);
            sb.AppendLine();

            sb.AppendLine($"static {className} {globalize("Lumina.Excel.IExcelRow")}<{className}>.Create({globalize("Lumina.Excel.ExcelPage")} page, uint offset, uint row) =>");
            using (sb.IndentScope())
            {
                if (!converter.HasSubrows)
                    sb.AppendLine("new(page, offset, row);");
                else
                    sb.AppendLine("throw new NotSupportedException();");
            }

            sb.AppendLine();

            sb.AppendLine($"static {className} {globalize("Lumina.Excel.IExcelRow")}<{className}>.Create({globalize("Lumina.Excel.ExcelPage")} page, uint offset, uint row, ushort subrow) =>");
            using (sb.IndentScope())
            {
                if (converter.HasSubrows)
                    sb.AppendLine("new(page, offset, row, subrow);");
                else
                    sb.AppendLine("throw new NotSupportedException();");
            }
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
