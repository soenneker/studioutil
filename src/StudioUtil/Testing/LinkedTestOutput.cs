using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace StudioUtil.Testing;

public sealed class LinkedTestOutput : UserControl
{
    private readonly BoundedTextBuffer _owned = new();
    private readonly ObservableCollection<OutputLogEntry> _lines = new();
    private readonly ListBox _list;
    private BoundedTextBuffer? _source;
    private long _version = -1, _generation, _removed;
    private int _length;
    private bool _fullLog;
    private TextWrapping _wrapping;
    private ScrollViewer? _scroll;
    internal event Action<OutputSourceLink>? SourceClicked;

    public LinkedTestOutput()
    {
        _list = new ListBox { ItemsSource = _lines, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Extended };
        _list.SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        var factory = new FrameworkElementFactory(typeof(OutputLogLine));
        _list.ItemTemplate = new DataTemplate { VisualTree = factory };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _list.ItemContainerStyle = style;
        Content = _list;
        Loaded += (_, _) => { _scroll = FindScroll(_list); RefreshLog(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) SetBuffer(_source ?? _owned); };
    }

    public string Text { get => (_source ?? _owned).GetText(); set { _owned.Clear(); _owned.Append(value ?? ""); _source = null; SetBuffer(_owned); } }
    public int TextLength => (_source ?? _owned).Length;
    public bool FullLog { get => _fullLog; set { if (_fullLog == value) return; _fullLog = value; RefreshLog(); } }
    public TextWrapping TextWrapping
    {
        get => _wrapping;
        set { if (_wrapping == value) return; _wrapping = value; ScrollViewer.SetHorizontalScrollBarVisibility(_list, value == TextWrapping.Wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto); _list.Items.Refresh(); }
    }
    public double VerticalOffset => _scroll?.VerticalOffset ?? 0;
    public double ExtentHeight => _scroll?.ExtentHeight ?? 0;
    public double ViewportHeight => _scroll?.ViewportHeight ?? 0;
    public void ScrollToEnd() => _scroll?.ScrollToEnd();
    public void ScrollToHome() => _scroll?.ScrollToHome();
    public void ScrollToHorizontalOffset(double value) => _scroll?.ScrollToHorizontalOffset(value);
    public void RefreshLog() { _version = -1; SetBuffer(_source ?? _owned); }
    public void AppendText(string text) { _owned.Append(text); SetBuffer(_owned); }
    public void Clear() => Text = "";
    internal void OpenSource(OutputSourceLink source) => SourceClicked?.Invoke(source);
    internal string FindNext(string query, bool previous = false)
    {
        if (string.IsNullOrEmpty(query)) return "";
        var matches = new List<int>();
        for (int i = 0; i < _lines.Count; i++)
            if (_lines[i].Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(i);
        if (matches.Count == 0) return "No matches";
        int match = previous ? matches.FindLastIndex(i => i < _list.SelectedIndex) : matches.FindIndex(i => i > _list.SelectedIndex);
        if (match < 0) match = previous ? matches.Count - 1 : 0;
        int index = matches[match];
        _list.SelectedIndex = index; _scroll?.ScrollToVerticalOffset(index);
        return $"{match + 1} / {matches.Count} lines";
    }

    internal void SetBuffer(BoundedTextBuffer source)
    {
        if (!IsVisible) { _source = source; _version = -1; return; }
        if (ReferenceEquals(_source, source) && _version == source.Version) return;
        bool reset = _version < 0 || !ReferenceEquals(_source, source) || _generation != source.Generation || _removed != source.Removed || source.Length < _length;
        string added = source.GetText(reset ? (_fullLog ? 0 : Math.Max(0, source.Length - OutputDisplayWindow.MaxCharacters)) : _length);
        if (reset)
        {
            _lines.Clear();
            if (!_fullLog) added = added.Substring(OutputDisplayWindow.Start(added));
            if (source.Removed > 0 || source.Length > added.Length) _lines.Add(new OutputLogEntry("Earlier output omitted from this view. Open the run folder for archived logs."));
            _lines.Add(new OutputLogEntry(""));
        }
        _source = source; _version = source.Version; _generation = source.Generation; _removed = source.Removed; _length = source.Length;
        string pending = (_lines.Count > 0 ? _lines[_lines.Count - 1].Text : "") + added;
        int start = 0;
        bool first = true;
        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i] != '\n') continue;
            AddLine(pending.Substring(start, i - start).TrimEnd('\r'), ref first);
            start = i + 1;
        }
        AddLine(pending.Substring(start), ref first);
        if (!_fullLog) while (_lines.Count > OutputDisplayWindow.MaxLines + 1) _lines.RemoveAt(0);
    }

    private void AddLine(string line, ref bool first)
    {
        if (first && _lines.Count > 0) _lines[_lines.Count - 1] = new OutputLogEntry(line);
        else _lines.Add(new OutputLogEntry(line));
        first = false;
    }

    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var found = FindScroll(VisualTreeHelper.GetChild(parent, i)); if (found != null) return found; }
        return null;
    }
}
