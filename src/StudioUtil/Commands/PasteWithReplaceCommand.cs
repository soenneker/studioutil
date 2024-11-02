using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Community.VisualStudio.Toolkit;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Dtos;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Humanizer;
using StudioUtil.Utils.Abstract;
using MessageBox = System.Windows.MessageBox;
using StudioUtil.Extensions;
using StudioUtil.Utils;

namespace StudioUtil.Commands;

#nullable enable

[Command(PackageIds.PasteWithReplaceCommand)]
public class PasteWithReplaceCommand : BaseDICommand
{
    private readonly IFileUtilSync _fileUtil;
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly IVariablesUtil _variablesUtil;
    private readonly ILogger<PasteWithReplaceCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public PasteWithReplaceCommand(DIToolkitPackage package, IFileUtilSync fileUtil, IDteUtil dteUtil, INewItemFactory newItemFactory, IVariablesUtil variablesUtil,
        ILogger<PasteWithReplaceCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
        _fileUtil = fileUtil;
        _dteUtil = dteUtil;
        _newItemFactory = newItemFactory;
        _variablesUtil = variablesUtil;
        _logger = logger;
        _solutionUtil = solutionUtil;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        _logger.LogInformation("Starting file creation from clipboard");

        // Get files from the clipboard
        var filesFromClipboard = GetFiles();

        if (filesFromClipboard.Count == 0)
        {
            MessageBox.Show("No files found in the clipboard.");
            return;
        }

        // Prompt for the target and replacement strings
        var targetVar = _variablesUtil.Get("target");
        var replacementVar = _variablesUtil.Get("replacement");
        var promptDto = PromptForFileName(targetVar, replacementVar);

        ValidateInput(promptDto);

        foreach (var kvp in filesFromClipboard)
        {
            var fileName = kvp.Key;
            var lines = kvp.Value;

            var replacements = new Dictionary<string, string>
            {
                {promptDto.Target!, promptDto.Replacement!}
            };

            // Perform additional camel-cased replacement
            var camelizedKey = promptDto.Target.Camelize();

            if (!replacements.ContainsKey(camelizedKey))
            {
                replacements.Add(camelizedKey, promptDto.Replacement.Camelize());
            }

            // Apply replacements to the lines
            lines.Replace(replacements);

            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte);
            var directory = Path.GetDirectoryName(target?.ProjectItem?.GetFileName());

            if (directory == null)
            {
                _logger.LogError("Failed to determine target directory.");
                throw new DirectoryNotFoundException("Could not determine target directory.");
            }

            var newFileName = fileName.Replace(promptDto.Target, promptDto.Replacement);

            var newFilePath = Path.Combine(directory, newFileName);

            // Write updated lines to the new file
            _fileUtil.WriteAllLines(newFilePath, lines);

            // Add new file to solution
            dte.Solution.AddFromFile(newFilePath);
        }

        await _solutionUtil.RefreshSolutionExplorer();
        await base.ExecuteAsync(e);
    }

    private Dictionary<string, List<string>> GetFiles()
    {
        var dict = new Dictionary<string, List<string>>();

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();

            foreach (string filePath in files)
            {
                try
                {
                    var fileContent = _fileUtil.ReadAllLines(filePath!);
                    var fileName = Path.GetFileName(filePath);
                    dict[fileName] = fileContent;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error reading file '{filePath}': {ex.Message}");
                }
            }
        }
        else
        {
            MessageBox.Show("Clipboard does not contain a file.");
        }

        return dict;
    }

    private void ValidateInput(PromptDto promptDto)
    {
        if (!promptDto.Result)
            throw new Exception("Messagebox was closed prematurely");

        if (string.IsNullOrEmpty(promptDto.Replacement))
            throw new ArgumentNullException(nameof(promptDto.Replacement));

        if (string.IsNullOrEmpty(promptDto.Target))
            throw new ArgumentNullException(nameof(promptDto.Target));
    }

    private static PromptDto PromptForFileName(string? target, string? replacement)
    {
        var dialog = new CloneAndReplaceDialog
        {
            Owner = Application.Current.MainWindow,
            Target = target,
            Replacement = replacement
        };

        var result = dialog.ShowDialog();

        return new PromptDto
        {
            Result = result.GetValueOrDefault(),
            Target = dialog.TargetInput,
            Replacement = dialog.ReplacementInput
        };
    }
}