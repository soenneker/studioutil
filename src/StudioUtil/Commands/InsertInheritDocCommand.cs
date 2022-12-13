using System;
using System.Linq;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using StudioUtil.Utils.Abstract;
using Community.VisualStudio.Toolkit.DependencyInjection.Core;
using Community.VisualStudio.Toolkit.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using EnvDTE80;
using Microsoft.VisualStudio.Text.Editor;
using StudioUtil.Utils;
using System.Text.RegularExpressions;

namespace StudioUtil.Commands;

#nullable enable

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

    public static string? GetInterfaceFromText(string text)
    {
        Regex regex = new(@"(:|,) (I[a-zA-Z]+)", RegexOptions.Compiled);

        var matches = regex.Match(text);

        if (matches != null && matches.Groups.Count == 3)
        {
            return matches.Groups[2].Value;
        }

        return null;
    }

    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        IWpfTextView view = ProjectHelpers.GetCurentTextView();

        if (view != null)
        {
            var dte = await _dteUtil.GetDte();

            InsertText(view, dte);
        }

        await base.ExecuteAsync(e);
    }
    private static string? GetNextLine(IWpfTextView view)
    {
        int caretLineNumber = view.TextSnapshot.GetLineNumberFromPosition(view.Caret.Position.BufferPosition);

        var lines = view.TextSnapshot.Lines.ToList();

        if (!lines.Any())
            return null;

        if (caretLineNumber > lines.Count)
            return null;

        var nextLine = lines[caretLineNumber + 1];

        return nextLine.GetText();
    }

    private static void InsertText(IWpfTextView view, DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            dte.UndoContext.Open("Insert text");

            using (Microsoft.VisualStudio.Text.ITextEdit edit = view.TextBuffer.CreateEdit())
            {
                if (!view.Selection.IsEmpty)
                {
                    edit.Delete(view.Selection.SelectedSpans[0].Span);
                    view.Selection.Clear();
                }

                var text = "///<inheritdoc/>";

                var nextLine = GetNextLine(view);

                if (!string.IsNullOrWhiteSpace(nextLine))
                {
                    var interText = GetInterfaceFromText(nextLine!);

                    if (interText != null)
                    {
                        text = $"///<inheritdoc cref=\"{interText}\"/>";
                    }
                }
                
                edit.Insert(view.Caret.Position.BufferPosition, text);
                edit.Apply();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.Write(ex);
        }
        finally
        {
            dte.UndoContext.Close();
        }
    }
}