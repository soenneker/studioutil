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
using System.Linq;
using StudioUtil.Utils.Abstract;
using MessageBox = System.Windows.MessageBox;
using StudioUtil.Testing;
using StudioUtil.Utils;

namespace StudioUtil.Commands;

#nullable enable

/// <summary>
/// Represents the paste with replace command.
/// </summary>
[Command(PackageIds.PasteWithReplaceCommand)]
public class PasteWithReplaceCommand : BaseDICommand
{
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly IVariablesUtil _variablesUtil;
    private readonly ILogger<PasteWithReplaceCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public PasteWithReplaceCommand(DIToolkitPackage package, IDteUtil dteUtil, INewItemFactory newItemFactory, IVariablesUtil variablesUtil,
        ILogger<PasteWithReplaceCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
        _dteUtil = dteUtil;
        _newItemFactory = newItemFactory;
        _variablesUtil = variablesUtil;
        _logger = logger;
        _solutionUtil = solutionUtil;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var sources = Clipboard.ContainsFileDropList() ? Clipboard.GetFileDropList().Cast<string>().ToList() : new List<string>();
            if (sources.Count == 0) { MessageBox.Show("No files found in the clipboard."); return; }
            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte) ?? throw new InvalidOperationException("Select a destination folder.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string directory = target.Directory ?? throw new DirectoryNotFoundException("Select a destination folder.");
            string solution = dte.Solution.FullName;
            var prompt = PromptForFileName(_variablesUtil.Get("target"), _variablesUtil.Get("replacement"));
            if (!prompt.Result) return;
            var paths = await WorkerClient.Shared.CloneFilesAsync(new FileCommandRequest
            {
                Sources = sources, Directory = directory, Target = prompt.Target, Replacement = prompt.Replacement
            });
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CommandFiles.Add(dte, target, solution, paths);
            await _solutionUtil.RefreshSolutionExplorer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not paste files.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            MessageBox.Show(ex.Message, "Paste files", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static ClonePromptDto PromptForFileName(string? target, string? replacement)
    {
        var dialog = new CloneAndReplaceDialog
        {
            Owner = Application.Current.MainWindow,
            Target = target,
            Replacement = replacement
        };

        var result = dialog.ShowDialog();

        return new ClonePromptDto
        {
            Result = result.GetValueOrDefault(),
            Target = dialog.TargetInput,
            Replacement = dialog.ReplacementInput
        };
    }
}
