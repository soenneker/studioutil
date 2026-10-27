using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StudioUtil.Testing;

namespace StudioUtil.Rendering.Tests;

public sealed class OutputRenderingTests
{
    [Test]
    public Task FullLogVirtualizesSearchesWrapsAndKeepsLinks() => OnSta(() =>
    {
        var output = new LinkedTestOutput { FullLog = true, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var window = new Window { Content = output, Width = 900, Height = 600, Left = -10000, Top = -10000, ShowInTaskbar = false };
        try
        {
            window.Show();
            output.Text = string.Concat(Enumerable.Range(0, 10000).Select(i => $"Log entry {i}\n"));
            output.UpdateLayout();
            var list = (ListBox)output.Content;
            Check(list.Items.Count == 10001, "Full history was truncated.");
            Check(CountRows(output) < 100, "Full history instantiated offscreen rows.");
            output.FindNext("Log entry 9876"); output.UpdateLayout();
            Check(((OutputLogEntry)list.SelectedItem).Text == "Log entry 9876", "Search missed an offscreen line.");
            output.TextWrapping = TextWrapping.Wrap; output.UpdateLayout();
            Check(CountRows(output) < 100, "Wrapping disabled virtualization.");
            output.Visibility = Visibility.Collapsed; output.AppendText("hidden output\n");
            output.Visibility = Visibility.Visible; output.UpdateLayout();
            Check(list.Items.Cast<OutputLogEntry>().Any(x => x.Text == "hidden output"), "Hidden output was lost.");
            output.Clear(); output.AppendText("at Example in C:\\source\\Test.cs:line 42\n"); output.ScrollToHome(); output.UpdateLayout();
            var row = FindRow(output);
            Check(row != null && row.Inlines.OfType<Hyperlink>().Any(), "Exception hyperlink missing.");
            output.FullLog = false; output.Text = string.Concat(Enumerable.Repeat("line\n", 2000)); output.UpdateLayout();
            Check(list.Items.Count <= 401, "Recent view exceeded its line budget.");
        }
        finally { window.Close(); }
    });

    private static int CountRows(DependencyObject parent)
    {
        int result = parent is OutputLogLine ? 1 : 0;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) result += CountRows(VisualTreeHelper.GetChild(parent, i));
        return result;
    }
    private static OutputLogLine? FindRow(DependencyObject parent)
    {
        if (parent is OutputLogLine line) return line;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var row = FindRow(VisualTreeHelper.GetChild(parent, i)); if (row != null) return row; }
        return null;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Task OnSta(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => { try { action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
}
