using Microsoft.CodeAnalysis;
using System.IO;
using System;
using System.Linq;

namespace ExdGenerator;

internal static class GeneratorUtils
{
    public static string GetFullName(this INamedTypeSymbol me) =>
        string.Join(".", me.ContainingNamespace.ConstituentNamespaces.Select(s => s.Name)) + "." + me.Name;
}
