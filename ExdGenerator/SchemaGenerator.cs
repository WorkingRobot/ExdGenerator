using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ExdGenerator;

[Generator]
public class SchemaGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {

    }

    public void Execute(GeneratorExecutionContext context)
    {
        context.AddSource("SheetSchemaAttribute.g.cs", SourceConstants.CreateAttributeSource("SheetSchema"));

        foreach(var (clazz, attribute) in GetAttributedClasses(context, "StreetSchema"))
        {
            string? schemaPath = (string?)attribute.ConstructorArguments.First().Value;
            if (schemaPath == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.InvalidSchemaPath, clazz.GetLocation()));
                continue;
            }

            
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(clazz.SyntaxTree.FilePath)), schemaPath);
        }
    }

    private IEnumerable<(ClassDeclarationSyntax, AttributeData)> GetAttributedClasses(GeneratorExecutionContext context, string attributeName) =>
        context.Compilation.SyntaxTrees.SelectMany(tree =>
        {
            var model = context.Compilation.GetSemanticModel(tree);
            return tree
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .Select(clazz =>
                    (clazz,
                    model
                        .GetDeclaredSymbol(clazz)?
                        .GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.GetFullName() == $"{SourceConstants.GeneratedNamespace}.{attributeName}Attribute"))
                    )
                .Where(t => t.Item2 != null);
        })!;
}
