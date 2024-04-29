using Microsoft.CodeAnalysis;

namespace ExdGenerator;

internal static class Diagnostics
{
    public static DiagnosticDescriptor InvalidSchemaPath => new("EXD001", "Schema path must be a string literal", "Schema path must be a string literal", "Schema", DiagnosticSeverity.Error, true);
}
