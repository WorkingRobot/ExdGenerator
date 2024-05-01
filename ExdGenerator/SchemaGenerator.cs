using ExdGenerator.Schema;
using Lumina;
using Lumina.Excel;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ExdGenerator;

[Generator]
public class SchemaGenerator : IIncrementalGenerator
{
    private GameData? GameData { get; set; }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(ctx =>
        {
            ctx.AddSource("SheetSchemaAttribute.g.cs", SourceConstants.CreateAttributeSource("SheetSchema"));
        });
        var fqmn = $"{SourceConstants.GeneratedNamespace}.SheetSchemaAttribute";

        var options = context.AnalyzerConfigOptionsProvider.Select((provider, _) =>
        {
            provider.GlobalOptions.TryGetValue("build_property.SchemaPath", out var schemaPath);
            provider.GlobalOptions.TryGetValue("build_property.GamePath", out var gamePath);
            provider.GlobalOptions.TryGetValue("build_property.GeneratedNamespace", out var generatedNamespace);
            return new GeneratorOptions
            {
                SchemaPath = schemaPath,
                GamePath = gamePath ?? throw new InvalidOperationException("GamePath must be set"),
                GeneratedNamespace = generatedNamespace
            };
        });

        var attributeMetadata = context.SyntaxProvider.ForAttributeWithMetadataName(fqmn, (node, _) => true, (ctx, _) =>
        {
            var data = ctx.Attributes.FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fqmn) ?? throw new InvalidOperationException("Attribute not found");
            if (data.ConstructorArguments[0].Value is not string schemaPath)
                throw new InvalidOperationException("Schema path must be a string literal");
            return (schemaPath, data, (INamedTypeSymbol)ctx.TargetSymbol);
        });

        context.RegisterSourceOutput(options, GenerateSchemas);
        //context.RegisterSourceOutput(attributeMetadata.Combine(options), GenerateSchema);
    }

    private GameData GetGameData(GeneratorOptions options) =>
        GameData ??= new(options.GamePath);

    private void GenerateSchema(SourceProductionContext context, ((string, AttributeData, INamedTypeSymbol), GeneratorOptions) args)
    {
        var ((schemaPath, attribute, symbol), options) = args;

        AdditionalFileStream? resolvedSchemaFile = null;
        List<string> attemptedFilePaths = [];

        var filePath = attribute.ApplicationSyntaxReference?.SyntaxTree.FilePath;
        if (filePath != null)
        {
            var fileSchemaPath = Path.GetFullPath(Path.Combine(filePath, "..", schemaPath));
            attemptedFilePaths.Add(fileSchemaPath);
            resolvedSchemaFile = AdditionalFileStream.TryOpen(fileSchemaPath);
        }

        if (resolvedSchemaFile == null && options.SchemaPath != null)
        {
            var fileSchemaPath = Path.GetFullPath(Path.Combine(options.SchemaPath, schemaPath));
            attemptedFilePaths.Add(fileSchemaPath);
            resolvedSchemaFile = AdditionalFileStream.TryOpen(fileSchemaPath);
        }

        if (resolvedSchemaFile == null)
        {
            context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SchemaNotFound, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation(), string.Join(", ", attemptedFilePaths)));
            return;
        }

        using var schema = resolvedSchemaFile;
        using var reader = new StreamReader(schema);

        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        var sheet = deserializer.Deserialize<Sheet>(reader);

        var converter = new SchemaSourceConverter(sheet, GetGameData(options));
        var source = SourceConstants.CreateSchemaSource(symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToString(), symbol.Name, false, converter);
        context.Debug($"{Convert.ToBase64String(Encoding.UTF8.GetBytes(source.ToString()))}");
        context.AddSource($"{symbol.Name}.g.cs", source);
    }

    private void GenerateSchemas(SourceProductionContext context, GeneratorOptions options)
    {
        var gameData = GetGameData(options);
        var sheets = gameData.Excel.GetSheetNames();
        foreach (var sheetName in sheets)
        {
            var fileSchemaPath = Path.GetFullPath(Path.Combine(options.SchemaPath, $"{sheetName}.yml"));
            var schemaFile = AdditionalFileStream.TryOpen(fileSchemaPath);
            if (schemaFile == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.SchemaNotFound, Location.None, DiagnosticSeverity.Warning, null, null, fileSchemaPath));
                continue;
            }

            using var schema = schemaFile;
            using var reader = new StreamReader(schema);

            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            var sheet = deserializer.Deserialize<Sheet>(reader);

            var converter = new SchemaSourceConverter(sheet, GetGameData(options));
            var source = SourceConstants.CreateSchemaSource(options.GeneratedNamespace, sheet.Name, true, converter);
            context.Debug($"{sheet.Name} -> {Convert.ToBase64String(Encoding.UTF8.GetBytes(source.ToString()))}");
            context.AddSource($"{sheet.Name}.g.cs", source);
        }
    }
}

public sealed record GeneratorOptions
{
    public required string? SchemaPath { get; init; }
    public required string GamePath { get; init; }
    public required string? GeneratedNamespace { get; init; }
}
