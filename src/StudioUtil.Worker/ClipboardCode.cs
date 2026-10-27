using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.RegularExpressions;

namespace StudioUtil.Testing;

internal static class ClipboardCode
{
    public static List<(string Path, string Text)> Generate(FileCommandRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 100_000)
            throw new ArgumentException("Clipboard must contain C# code of at most 100,000 characters.");
        var root = CSharpSyntaxTree.ParseText(request.Text, cancellationToken: token).GetCompilationUnitRoot(token);
        var result = new List<(string Path, string Text)>();
        foreach (var type in root.DescendantNodes().OfType<MemberDeclarationSyntax>().Where(m =>
                     m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax && m.Parent is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax))
        {
            token.ThrowIfCancellationRequested();
            string name = type is BaseTypeDeclarationSyntax declaration ? declaration.Identifier.ValueText : ((DelegateDeclarationSyntax)type).Identifier.ValueText;
            MemberDeclarationSyntax member = type;
            foreach (var ancestor in type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>())
                member = ancestor.WithMembers(SyntaxFactory.SingletonList(member));
            if (member == type)
                member = SyntaxFactory.FileScopedNamespaceDeclaration(SyntaxFactory.ParseName(GetNamespace(request)))
                    .WithMembers(SyntaxFactory.SingletonList(member));
            var document = root.WithMembers(SyntaxFactory.SingletonList(member));
            var inferred = CodeInference.Infer(document.ToFullString(), request.References);
            var usings = document.Usings.Concat(SyntaxFactory.ParseCompilationUnit(string.Join(Environment.NewLine, inferred)).Usings)
                .DistinctBy(u => u.WithoutTrivia().ToString());
            document = document.WithUsings(SyntaxFactory.List(usings));
            result.Add((FileCommands.Destination(request.Directory!, name + ".cs"), document.NormalizeWhitespace().ToFullString() + Environment.NewLine));
        }
        if (result.Count == 0) throw new InvalidOperationException("No top-level C# type declarations found in the clipboard.");
        return result;
    }

    private static string GetNamespace(FileCommandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectDirectory) || string.IsNullOrWhiteSpace(request.RootNamespace)) return "Generated";
        string relative = Path.GetRelativePath(request.ProjectDirectory, request.Directory!);
        if (relative == ".") return request.RootNamespace;
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return request.RootNamespace;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Select(part =>
        {
            string clean = Regex.Replace(part, @"[^\w]", "_");
            if (char.IsDigit(clean[0])) clean = "_" + clean;
            return SyntaxFacts.GetKeywordKind(clean) == SyntaxKind.None ? clean : "@" + clean;
        });
        return request.RootNamespace + "." + string.Join(".", parts);
    }
}
