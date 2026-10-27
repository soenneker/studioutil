using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using Community.VisualStudio.Toolkit;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Testing;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;

namespace StudioUtil.Commands;

[Command(PackageIds.CreateNewFilesFromClipboardCommand)]
public class CreateNewFilesFromClipboardCommand : BaseDICommand
{
    private readonly IDteUtil _dteUtil;
    private readonly INewItemFactory _newItemFactory;
    private readonly ILogger<CreateNewFilesFromClipboardCommand> _logger;
    private readonly ISolutionUtil _solutionUtil;

    public CreateNewFilesFromClipboardCommand(DIToolkitPackage package, IDteUtil dteUtil, INewItemFactory newItemFactory,
        ILogger<CreateNewFilesFromClipboardCommand> logger, ISolutionUtil solutionUtil) : base(package)
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
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) { MessageBox.Show("Clipboard does not contain C# code."); return; }
            var dte = await _dteUtil.GetDte();
            var target = await _newItemFactory.Create(dte) ?? throw new InvalidOperationException("Select a destination folder.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string solution = dte.Solution.FullName;
            string directory = target.Directory ?? throw new DirectoryNotFoundException("Select a destination folder.");
            string? projectRoot = null;
            string? rootNamespace = null;
            if (target.Project != null && !target.IsSolutionOrSolutionFolder)
            {
                projectRoot = Path.GetDirectoryName(target.Project.FullName);
                try { rootNamespace = target.Project.Properties?.Item("DefaultNamespace")?.Value as string; }
                catch (ArgumentException) { }
                catch (System.Runtime.InteropServices.COMException) { }
                rootNamespace ??= target.Project.Name;
            }
            var request = new FileCommandRequest
            {
                Text = text, Directory = directory, ProjectDirectory = projectRoot, RootNamespace = rootNamespace,
                References = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => a.Location).Distinct().ToList()
            };
            var paths = await WorkerClient.Shared.GenerateFilesAsync(request);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            CommandFiles.Add(dte, target, solution, paths);
            await _solutionUtil.RefreshSolutionExplorer();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not create files from clipboard.");
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            MessageBox.Show(ex.Message, "Create files", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
