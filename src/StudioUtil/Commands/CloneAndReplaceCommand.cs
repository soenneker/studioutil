using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Community.VisualStudio.Toolkit;
using StudioUtil.Dtos;
using StudioUtil.Testing;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;

using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil.Commands;

#nullable enable

/// <summary>
/// Represents the clone and replace command.
/// </summary>
[Command(PackageIds.CloneAndReplaceCommand)]
public class CloneAndReplaceCommand : BaseDICommand
{
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<CloneAndReplaceCommand> _logger;
    private readonly IVariablesUtil _variablesUtil;
    private readonly ISolutionUtil _solutionUtil;

    public CloneAndReplaceCommand(DIToolkitPackage package, IDteUtil dteUtil, INewItemFactory newItemFactory, IVariablesUtil variablesUtil,
        ILogger<CloneAndReplaceCommand> logger, ISolutionUtil solutionUtil) : base(package)
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
            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte) ?? throw new InvalidOperationException("Select a source file.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string source = target.ProjectItem?.GetFileName() ?? throw new InvalidOperationException("Select a source file.");
            string solution = dte.Solution.FullName;
            var prompt = PromptForFileName(_variablesUtil.Get("target"), _variablesUtil.Get("replacement"));
            if (!prompt.Result) return;
            var paths = await WorkerClient.Shared.CloneFilesAsync(new FileCommandRequest
            {
                Sources = new List<string> { source }, Target = prompt.Target, Replacement = prompt.Replacement
            });
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CommandFiles.Add(dte, target, solution, paths);
            await _solutionUtil.RefreshSolutionExplorer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not clone file.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            MessageBox.Show(ex.Message, "Clone file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static ClonePromptDto PromptForFileName(string? target, string? replacement)
    {
        var dialog = new CloneAndReplaceDialog
        {
            //IntPtr hwnd = new IntPtr(_dte.MainWindow.HWnd);
            //System.Windows.Window window = (System.Windows.Window)HwndSource.FromHwnd(hwnd).RootVisual;
            Owner = Application.Current.MainWindow,
            Target = target,
            Replacement = replacement
        };
        
        var result = dialog.ShowDialog();

        var dto = new ClonePromptDto
        {
            Result = result.GetValueOrDefault(),
            Target = dialog.TargetInput,
            Replacement = dialog.ReplacementInput
        };

        return dto;
    }
}
