using System;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Testing;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;

namespace StudioUtil.Commands;

[Command(PackageIds.InsertInheritDocCommand)]
public class InsertInheritDocCommand : BaseDICommand
{
    private readonly ILogger<InsertInheritDocCommand> _logger;
    private readonly IDteUtil _dteUtil;

    public InsertInheritDocCommand(DIToolkitPackage package, ILogger<InsertInheritDocCommand> logger, IDteUtil dteUtil) : base(package)
    {
        _logger = logger;
        _dteUtil = dteUtil;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var view = ProjectHelpers.GetCurentTextView();
            if (view == null) return;
            var dte = await _dteUtil.GetDte();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var snapshot = view.TextSnapshot;
            int position = view.Caret.Position.BufferPosition.Position;
            var selection = view.Selection.StreamSelectionSpan.SnapshotSpan.Span;
            int nextLine = snapshot.GetLineNumberFromPosition(position) + 1;
            string declaration = nextLine < snapshot.LineCount ? snapshot.GetLineFromLineNumber(nextLine).GetText() : "";
            string text = await WorkerClient.Shared.DocumentationAsync(declaration);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            // Never apply an asynchronously prepared edit to a changed document or selection.
            if (view.IsClosed || view.TextSnapshot != snapshot || view.Caret.Position.BufferPosition.Position != position
                || view.Selection.StreamSelectionSpan.SnapshotSpan.Span != selection) return;
            dte.UndoContext.Open("Insert documentation");
            try
            {
                using var edit = view.TextBuffer.CreateEdit();
                if (selection.Length > 0) edit.Replace(selection, text);
                else edit.Insert(position, text);
                edit.Apply();
            }
            finally { dte.UndoContext.Close(); }
        }
        catch (Exception ex) { _logger.LogError(ex, "Could not insert documentation."); }
    }
}
