using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace StudioUtil.Testing;

public sealed class OutputLogLine : TextBlock
{
    public OutputLogLine()
    {
        DataContextChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    private LinkedTestOutput? Owner()
    {
        DependencyObject? parent = this;
        while (parent != null) { if (parent is LinkedTestOutput output) return output; parent = VisualTreeHelper.GetParent(parent); }
        return null;
    }

    private void Render()
    {
        Inlines.Clear();
        TextWrapping = Owner()?.TextWrapping ?? TextWrapping.NoWrap;
        if (!(DataContext is OutputLogEntry entry)) return;
        string text = entry.Text;
        // WPF instantiates these controls only for visible/recycled rows.
        int position = 0;
        if (text.Length <= 4096)
        foreach (var source in OutputSourceLinks.Parse(text))
        {
            if (source.Start > position) Inlines.Add(new Run(text.Substring(position, source.Start - position)));
            var link = new Hyperlink(new Run(text.Substring(source.Start, source.Length))) { ToolTip = $"Open {source.Path}:{source.Line}" };
            link.SetResourceReference(ForegroundProperty, EnvironmentColors.PanelHyperlinkBrushKey);
            link.Click += (_, e) => { e.Handled = true; Owner()?.OpenSource(source); };
            Inlines.Add(link); position = source.Start + source.Length;
        }
        if (position < text.Length) Inlines.Add(new Run(text.Substring(position)));
        if (text.Length == 0) Inlines.Add(new Run(" "));
    }
}
