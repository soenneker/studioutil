using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;
using EnvDTE80;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using MessageBox = System.Windows.MessageBox;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace StudioUtil.Commands;

#nullable enable

[Command(PackageIds.CreateNewFilesFromClipboardCommand)]
public class CreateNewFilesFromClipboardCommand : BaseDICommand
{
    private readonly IFileUtilSync _fileUtil;
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<CreateNewFilesFromClipboardCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public CreateNewFilesFromClipboardCommand(DIToolkitPackage package, IFileUtilSync fileUtil, IDteUtil dteUtil, INewItemFactory newItemFactory,
        ILogger<CreateNewFilesFromClipboardCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
        _fileUtil = fileUtil;
        _dteUtil = dteUtil;
        _newItemFactory = newItemFactory;
        _logger = logger;
        _solutionUtil = solutionUtil;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            _logger.LogInformation("Starting clipboard processing for type extraction.");

            var clipboardText = Clipboard.GetText();
            if (string.IsNullOrEmpty(clipboardText))
            {
                _logger.LogWarning("Clipboard is empty or contains no text.");
                MessageBox.Show("Clipboard is empty or does not contain text.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (clipboardText.Length > 100_000)
            {
                _logger.LogError("Clipboard content too large.");
                MessageBox.Show("Clipboard content is too large to process safely.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var definitions = SplitIntoTopLevelTypes(clipboardText);

            if (!definitions.Any())
            {
                _logger.LogWarning("No recognizable type definitions found.");
                MessageBox.Show("No type definitions found in the clipboard content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte);
            var directory = Path.GetDirectoryName(target?.ProjectItem?.GetFileName());

            if (directory == null)
            {
                _logger.LogError("Failed to determine target directory.");
                throw new DirectoryNotFoundException("Could not determine target directory.");
            }

            foreach (var def in definitions)
            {
                await CreateFileForDefinitionAsync(dte, def, directory);
            }

            await _solutionUtil.RefreshSolutionExplorer();
            _logger.LogInformation("Finished generating files from clipboard content.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in CreateNewFilesFromClipboardCommand.");
            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<string> SplitIntoTopLevelTypes(string clipboardText)
    {
        _logger.LogInformation("Splitting clipboard content into top-level type definitions.");

        var lines = clipboardText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var definitions = new List<string>();
        var currentBlock = new List<string>();
        var braceDepth = 0;
        var inType = false;

        var typePattern = new Regex(@"\b(class|struct|record|interface|enum|delegate)\b");

        foreach (var line in lines)
        {
            if (!inType && typePattern.IsMatch(line))
            {
                inType = true;
                braceDepth = 0;
                currentBlock.Clear();
            }

            if (inType)
            {
                currentBlock.Add(line);

                foreach (var c in line)
                {
                    if (c == '{') braceDepth++;
                    else if (c == '}') braceDepth--;
                }

                if (braceDepth == 0 && (line.Trim().EndsWith(";") || currentBlock.Any(l => l.Contains("{"))))
                {
                    definitions.Add(string.Join(Environment.NewLine, currentBlock));
                    inType = false;
                }
            }
        }

        return definitions;
    }

    private async Task CreateFileForDefinitionAsync(DTE2? dte, string definition, string directory)
    {
        try
        {
            var match = Regex.Match(definition, @"\b(class|struct|record|interface|enum|delegate)\s+([\w\d_]+)");

            if (!match.Success)
            {
                _logger.LogWarning("Could not extract type name from definition.");
                return;
            }

            var typeName = match.Groups[2].Value;
            var filePath = Path.Combine(directory, $"{typeName}.cs");

            var usings = GetInferredUsings(definition);
            var content = new List<string>(usings) { "" };

            if (!Regex.IsMatch(definition, @"^\s*namespace\s+", RegexOptions.Multiline))
            {
                var namespaceName = await GetNamespaceFromPath(directory);
                content.Add($"namespace {namespaceName};");
                content.Add(definition);
            }
            else
            {
                content.Add(definition);
            }

            _fileUtil.WriteFile(filePath, string.Join(Environment.NewLine, content));
            _logger.LogInformation($"Wrote {typeName} to {filePath}");

            dte?.Solution.AddFromFile(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating file from definition.");
        }
    }

    private async ValueTask<string> GetNamespaceFromPath(string directory)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        string? projectRoot = null;
        string? projectNamespace = null;

        var dte = await _dteUtil.GetDte();
        var solution = dte.Solution;

        foreach (EnvDTE.Project proj in solution.Projects)
        {
            var projDir = Path.GetDirectoryName(proj.FullName);

            if (!string.IsNullOrWhiteSpace(projDir) &&
                Path.GetFullPath(directory).StartsWith(Path.GetFullPath(projDir), StringComparison.OrdinalIgnoreCase))
            {
                projectRoot = projDir;

                try
                {
                    // Default to project name if RootNamespace property is missing
                    projectNamespace = proj.Properties?.Item("DefaultNamespace")?.Value as string
                                       ?? proj.Name;
                }
                catch
                {
                    projectNamespace = proj.Name;
                }

                break;
            }
        }

        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(projectNamespace))
            return "Generated";

        var relative = GetRelativePath(projectRoot, directory);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Select(SanitizeNamespacePart);

        return projectNamespace + (parts.Any() ? "." + string.Join(".", parts) : string.Empty);
    }

    private string SanitizeNamespacePart(string input)
    {
        var clean = Regex.Replace(input, @"[^\w]", "_");
        if (char.IsDigit(clean[0]))
            clean = "_" + clean;

        return clean;
    }

    private static readonly Dictionary<string, string> KnownAttributeUsings = new(StringComparer.OrdinalIgnoreCase)
{
    { "JsonPropertyName", "System.Text.Json.Serialization" },
    { "JsonIgnore", "System.Text.Json.Serialization" },
    { "JsonConverter", "System.Text.Json.Serialization" },
    { "JsonInclude", "System.Text.Json.Serialization" },
    { "JsonExtensionData", "System.Text.Json.Serialization" }
};

    private List<string> GetInferredUsings(string code)
    {
        var usings = new HashSet<string>();

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var root = syntaxTree.GetRoot();

        // Force-load known assemblies (NuGet types)
        _ = typeof(System.Text.Json.JsonSerializer);

        // ✅ Dynamically load all non-dynamic, non-null, non-empty assemblies
        var assemblyLocations = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a =>
                !a.IsDynamic &&
                !string.IsNullOrWhiteSpace(a.Location) &&
                File.Exists(a.Location))
            .Select(a => a.Location)
            .Distinct();

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

    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(basePath)));
        var fullUri = new Uri(Path.GetFullPath(fullPath));
        var relativeUri = baseUri.MakeRelativeUri(fullUri);
        var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string AppendDirectorySeparatorChar(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
