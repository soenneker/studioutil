using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Community.VisualStudio.Toolkit;
using StudioUtil.Dtos;
using StudioUtil.Extensions;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Humanizer;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil.Commands;

#nullable enable

[Command(PackageIds.CloneAndReplaceCommand)]
public class CloneAndReplaceCommand : BaseDICommand
{
    private readonly IFileUtilSync _fileUtil;
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<CloneAndReplaceCommand> _logger;
    private readonly IVariablesUtil _variablesUtil;
    private readonly ISolutionUtil _solutionUtil;

    public CloneAndReplaceCommand(DIToolkitPackage package, IFileUtilSync fileUtil, IDteUtil dteUtil, INewItemFactory newItemFactory, IVariablesUtil variablesUtil,
        ILogger<CloneAndReplaceCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
        _fileUtil = fileUtil;
        _dteUtil = dteUtil;
        _newItemFactory = newItemFactory;
        _variablesUtil = variablesUtil;
        _logger = logger;
        _solutionUtil = solutionUtil;
    }

    // TODO: this needs breaking up, organization, tests, etc
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        _logger.LogInformation("Beginning to log");

        var dte = await _dteUtil.GetDte();

        var target = await _newItemFactory.Create(dte);

        var fileName = target!.ProjectItem!.GetFileName();

        var targetVar = _variablesUtil.Get("target");

        var replacementVar = _variablesUtil.Get("replacement");

        var promptDto = PromptForFileName(targetVar, replacementVar);

        ValidateInput(promptDto);

        var lines = _fileUtil.ReadAllLines(fileName!);

        var replacements = new Dictionary<string, string>
        {
            {
                promptDto.Target!,
                promptDto.Replacement!
            },
        };

        var camelizedKey = promptDto.Target.Camelize();

        
        if (!replacements.ContainsKey(camelizedKey))
        {
            replacements.Add(camelizedKey,
                promptDto.Replacement.Camelize());
        }

        lines.Replace(replacements);

        var justFile = Path.GetFileName(fileName);
        var directory = Path.GetDirectoryName(fileName);

        var newFileName = justFile.Replace(promptDto.Target, promptDto.Replacement);

        var newFilePath = Path.Combine(directory, newFileName);

        _fileUtil.WriteAllLines(newFilePath, lines);

        // TODO: add support for project types != .NET Core 3+

        dte.Solution.AddFromFile(newFilePath);

        await _solutionUtil.RefreshSolutionExplorer();

        await base.ExecuteAsync(e);
    }

    private static void ValidateInput(ClonePromptDto clonePromptDto)
    {
        if (!clonePromptDto.Result)
            throw new Exception("Messagebox was early exited");

        if (string.IsNullOrEmpty(clonePromptDto.Replacement))
            throw new ArgumentNullException(nameof(clonePromptDto.Replacement));

        if (string.IsNullOrEmpty(clonePromptDto.Target))
            throw new ArgumentNullException(nameof(clonePromptDto.Target));
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