using System.Linq;
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace StudioUtil.Testing;
internal static class TestSourceDiscovery
{
    public static IReadOnlyList<TestDeclaration> Discover(string file, string text, CancellationToken token)
    {
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: token);
        var declarations = new List<TestDeclaration>();
        foreach (var method in tree.GetRoot(token).DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            token.ThrowIfCancellationRequested();
            if (!IsTest(method)) continue;
            var span = tree.GetLineSpan(method.Span, token);
            declarations.Add(new TestDeclaration(method.Identifier.SpanStart,
                new TestSourceTarget(file, method.Identifier.ValueText, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1)));
        }
        return declarations;
    }

    private static bool IsTest(MethodDeclarationSyntax method) => method.AttributeLists.SelectMany(a => a.Attributes)
        .Any(a => a.Name.ToString().Split('.').Last() is "Test" or "TestAttribute");
}
