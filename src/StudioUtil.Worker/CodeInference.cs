using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace StudioUtil.Testing;
internal static class CodeInference
{
    private static readonly Dictionary<string, string> KnownAttributeUsings = new(StringComparer.OrdinalIgnoreCase)
{
    { "JsonPropertyName", "System.Text.Json.Serialization" },
    { "JsonIgnore", "System.Text.Json.Serialization" },
    { "JsonConverter", "System.Text.Json.Serialization" },
    { "JsonInclude", "System.Text.Json.Serialization" },
    { "JsonExtensionData", "System.Text.Json.Serialization" }
};

    public static List<string> Infer(string code, IReadOnlyList<string> assemblyLocations)
    {
        var usings = new HashSet<string>();

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot();

        var references = assemblyLocations
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Inference",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var model = compilation.GetSemanticModel(syntaxTree, true);

        // 1. Identifier and qualified names
        var typeNodes = root.DescendantNodes().OfType<IdentifierNameSyntax>().Cast<SyntaxNode>()
            .Concat(root.DescendantNodes().OfType<QualifiedNameSyntax>());

        foreach (var node in typeNodes)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;

            INamespaceSymbol? ns = symbol switch
            {
                INamedTypeSymbol typeSym => typeSym.ContainingNamespace,
                IMethodSymbol methodSym => methodSym.ContainingType?.ContainingNamespace,
                IPropertySymbol propSym => propSym.ContainingType?.ContainingNamespace,
                IFieldSymbol fieldSym => fieldSym.ContainingType?.ContainingNamespace,
                ILocalSymbol localSym => localSym.Type.ContainingNamespace,
                _ => null
            };

            if (ns is { IsGlobalNamespace: false })
            {
                var nsName = ns.ToDisplayString();
                if (!string.IsNullOrWhiteSpace(nsName))
                    usings.Add($"using {nsName};");
            }
        }

        // 2. Attributes (semantic + fallback)
        var attributeNodes = root.DescendantNodes().OfType<AttributeSyntax>();

        foreach (var attr in attributeNodes)
        {
            var typeInfo = model.GetTypeInfo(attr.Name);
            var type = typeInfo.Type as INamedTypeSymbol;

            if (type?.ContainingNamespace is { IsGlobalNamespace: false } ns)
            {
                var nsName = ns.ToDisplayString();
                if (!string.IsNullOrWhiteSpace(nsName))
                {
                    usings.Add($"using {nsName};");
                    continue;
                }
            }

            var attrName = attr.Name.ToString();
            foreach (var kvp in KnownAttributeUsings)
            {
                if (attrName.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    usings.Add($"using {kvp.Value};");
                    break;
                }
            }
        }

        return usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct()
            .OrderBy(u => u)
            .ToList();
    }

}
