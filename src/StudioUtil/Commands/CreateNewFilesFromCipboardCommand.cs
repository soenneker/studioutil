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
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        _logger.LogInformation("Starting clipboard processing for class creation.");

        // Get clipboard content
        var clipboardText = Clipboard.GetText();
        if (string.IsNullOrEmpty(clipboardText))
        {
            _logger.LogWarning("Clipboard is empty or contains no text.");
            MessageBox.Show("Clipboard is empty or does not contain text.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Split clipboard text into class definitions
        var classDefinitions = SplitIntoClasses(clipboardText);
        if (!classDefinitions.Any())
        {
            _logger.LogWarning("No class definitions found in clipboard content.");
            MessageBox.Show("No class definitions found in the clipboard content.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Get target project information
        var dte = await _dteUtil.GetDte();
        var target = await _newItemFactory.Create(dte);
        var directory = Path.GetDirectoryName(target?.ProjectItem?.GetFileName());
        if (directory == null)
        {
            _logger.LogError("Failed to determine target directory.");
            throw new DirectoryNotFoundException("Could not determine target directory.");
        }

        // Create files for each class
        foreach (var classDefinition in classDefinitions)
        {
            CreateFileForClass(dte, classDefinition, directory);
        }

        await _solutionUtil.RefreshSolutionExplorer();
        _logger.LogInformation("Finished creating files from clipboard content.");
    }

    private List<string> SplitIntoClasses(string clipboardText)
    {
        _logger.LogInformation("Splitting clipboard content into class definitions and preserving all types of comments.");

        // Refined regex to capture class definitions along with all comment types: single-line, multi-line, XML documentation comments.
        var classRegex = new Regex(
            @"((\/\/.*|\/\*[\s\S]*?\*\/|\s*///.*|<summary>[\s\S]*?<\/summary>)\s*)*(public|internal|private|protected)?\s*(sealed|abstract)?\s*(partial)?\s*class\s+\w+.*?{(?>[^{}]+|{(?<depth>)|}(?<-depth>))*(?(depth)(?!))}",
            RegexOptions.Singleline);

        var matches = classRegex.Matches(clipboardText);

        // Convert the MatchCollection to a list of strings
        var classDefinitions = matches.Cast<Match>().Select(m => m.Value.Trim()).ToList();

        if (classDefinitions.Count == 0)
        {
            _logger.LogWarning("No class definitions found in the clipboard content.");
        }

        return classDefinitions;
    }

    private void CreateFileForClass(DTE2? dte, string classDefinition, string directory)
    {
        try
        {
            _logger.LogInformation("Creating file for class definition.");

            // Extract the class name to use as the filename
            var classNameMatch = Regex.Match(classDefinition, @"class\s+([\w\d]+)");

            if (!classNameMatch.Success)
            {
                _logger.LogWarning("Could not extract class name from class definition.");
                return;
            }

            var className = classNameMatch.Groups[1].Value;
            var newFilePath = Path.Combine(directory, $"{className}.cs");

            // Write the class definition to a new file
            _fileUtil.WriteFile(newFilePath, classDefinition);
            _logger.LogInformation($"Class {className} written to {newFilePath}.");

            dte?.Solution.AddFromFile(newFilePath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error adding file");
        }
    }
}
