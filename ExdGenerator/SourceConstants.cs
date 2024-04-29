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
}
