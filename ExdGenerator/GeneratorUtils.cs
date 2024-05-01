using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text.Encodings.Web;

namespace ExdGenerator;

internal static class GeneratorUtils
{
    public static string GetFullName(this INamedTypeSymbol me) =>
        string.Join(".", me.ContainingNamespace.ConstituentNamespaces.Select(s => s.Name)) + "." + me.Name;

    public static void Debug(this SourceProductionContext context, string message)
    {
        context.ReportDiagnostic(Diagnostic.Create("EXD000", "Debug", message, DiagnosticSeverity.Warning, DiagnosticSeverity.Warning, true, 1));
    }

    public static string EscapeStringToken(string text) =>
        $"\"{JavaScriptEncoder.UnsafeRelaxedJsonEscaping.Encode(text)}\"";
}
