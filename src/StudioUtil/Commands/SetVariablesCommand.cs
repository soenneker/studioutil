using System;
using System.Threading.Tasks;
using System.Windows;
using Community.VisualStudio.Toolkit;
using StudioUtil.Dtos;
using StudioUtil.Utils.Abstract;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil.Commands;

#nullable enable

[Command(PackageIds.SetVariablesCommand)]
public class SetVariablesCommand : BaseDICommand
{
    private readonly IVariablesUtil _variablesUtil;
    private readonly ILogger<SetVariablesCommand> _logger;

    public SetVariablesCommand(DIToolkitPackage package, IVariablesUtil variablesUtil, ILogger<SetVariablesCommand> logger) : base(package)
    {
        _variablesUtil = variablesUtil;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        var target = _variablesUtil.Get("target");
        var replacement = _variablesUtil.Get("replacement");

        var promptDto = Prompt(target, replacement);

        ValidateInput(promptDto);

        _variablesUtil.Set("target", promptDto.Target!);
        _variablesUtil.Set("replacement", promptDto.Replacement!);
            
        await base.ExecuteAsync(e);
    }

    private static void ValidateInput(ClonePromptDto clonePromptDto)
    {
        if (!clonePromptDto.Result)
            throw new Exception("Messagebox was early exited");
    }

    private static ClonePromptDto Prompt(string? target, string? replacement)
    {
        var dialog = new SetVariablesDialog
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