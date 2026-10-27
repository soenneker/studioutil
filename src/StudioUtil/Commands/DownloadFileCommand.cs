using Community.VisualStudio.Toolkit;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Dtos;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using System;
using System.IO;
using StudioUtil.Testing;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace StudioUtil.Commands;

#nullable enable

/// <summary>
/// Represents the download file command.
/// </summary>
[Command(PackageIds.DownloadFileCommand)]
public class DownloadFileCommand : BaseDICommand
{
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<DownloadFileCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public DownloadFileCommand(DIToolkitPackage package, IDteUtil dteUtil, INewItemFactory newItemFactory,
        ILogger<DownloadFileCommand> logger, ISolutionUtil solutionUtil) : base(package)
    {
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
            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte) ?? throw new InvalidOperationException("Select a destination folder.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string directory = target.Directory ?? throw new DirectoryNotFoundException("Select a destination folder.");
            string solution = dte.Solution.FullName;
            var prompt = PromptForUri(GetUriIfClipboardAlreadyContains(Clipboard.GetText()));
            if (!prompt.Result) return;
            string path = await WorkerClient.Shared.DownloadFileAsync(prompt.Target!, directory);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CommandFiles.Add(dte, target, solution, new[] { path });
            await _solutionUtil.RefreshSolutionExplorer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not download file.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            MessageBox.Show(ex.Message, "Download file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static string GetUriIfClipboardAlreadyContains(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
            return "";

        // Prefill the dialog from an HTTP(S) URL in the clipboard.
        const string urlPattern = @"\b(?:https?)://[^\s""']+";

        var match = Regex.Match(clipboardText, urlPattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "";
    }

    private static DownloadFileDto PromptForUri(string? target)
    {
        var dialog = new DownloadFileDialog
        {
            Owner = Application.Current.MainWindow,
            Target = target
        };

        var result = dialog.ShowDialog();

        var dto = new DownloadFileDto
        {
            Result = result.GetValueOrDefault(),
            Target = dialog.TargetInput
        };

        return dto;
    }

}
