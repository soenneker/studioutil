using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;

namespace StudioUtil.Testing;

public sealed class LinkedTestOutput : RichTextBox
{
    private readonly BoundedTextBuffer _owned = new();
    private BoundedTextBuffer? _source;
    private long _version = -1;
    private long _removed;
    private long _generation;
    private int _length;
    private string _tail = "";
    private Paragraph _paragraph = new() { Margin = new Thickness(0) };
    private Inline? _tailMarker;
    private Typeface? _typeface;
    private TextWrapping _wrapping = TextWrapping.NoWrap;
    private double _textWidth;
    internal event Action<OutputSourceLink>? SourceClicked;

    public LinkedTestOutput()
    {
        IsReadOnly = true;
        IsUndoEnabled = false;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        SetResourceReference(ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        Document.PagePadding = new Thickness(0);
        Document.ColumnWidth = double.PositiveInfinity;
        SizeChanged += (_, _) => UpdatePageWidth();
    }

    public string Text
    {
        get => (_source ?? _owned).GetText();
        set
        {
            _owned.Clear(); _owned.Append(value ?? "");
            _source = null;
            SetBuffer(_owned);
        }
    }

    public int TextLength => (_source ?? _owned).Length;

    internal void SetBuffer(BoundedTextBuffer source)
    {
        if (ReferenceEquals(_source, source) && _version == source.Version) return;
        bool reset = !ReferenceEquals(_source, source) || _generation != source.Generation || _removed != source.Removed || source.Length < _length;
        string added = source.GetText(reset ? 0 : _length);
        _source = source; _version = source.Version; _removed = source.Removed; _length = source.Length;
        _generation = source.Generation;
        BeginChange();
        try
        {
            if (reset)
            {
                Document.Blocks.Clear();
                _paragraph = new Paragraph { Margin = new Thickness(0) };
                Document.Blocks.Add(_paragraph);
                _tail = ""; _tailMarker = null; _textWidth = 0;
                if (source.Removed > 0) _paragraph.Inlines.Add(new Run("[Earlier output omitted]\n"));
            }
            while (_tailMarker != null)
            {
                var next = _tailMarker.NextInline;
                _paragraph.Inlines.Remove(_tailMarker);
                _tailMarker = next;
            }
            string pending = _tail + added;
            int start = 0;
            for (int i = 0; i < pending.Length; i++)
            {
                if (pending[i] != '\n') continue;
                RenderLine(pending.Substring(start, i - start + 1), true);
                start = i + 1;
            }
            _tail = pending.Substring(start);
            // Bound reparsing of pathological output with no newline. Completed prefixes stay plain text.
            if (_tail.Length > 32_768)
            {
                RenderLine(_tail.Substring(0, _tail.Length - 16_384), false);
                _tail = _tail.Substring(_tail.Length - 16_384);
            }
            _tailMarker = new Run();
            _paragraph.Inlines.Add(_tailMarker);
            RenderLine(_tail, true);
            UpdatePageWidth();
        }
        finally { EndChange(); }
    }

    private void RenderLine(string text, bool parse)
    {
        int position = 0;
        if (parse)
        {
            foreach (var source in OutputSourceLinks.Parse(text))
            {
                if (source.Start > position) _paragraph.Inlines.Add(new Run(text.Substring(position, source.Start - position)));
                var link = new Hyperlink(new Run(text.Substring(source.Start, source.Length)))
                { ToolTip = $"Open {source.Path}, line {source.Line}", Cursor = System.Windows.Input.Cursors.Hand, Tag = source };
                link.SetResourceReference(TextElement.ForegroundProperty, EnvironmentColors.PanelHyperlinkBrushKey);
                link.Click += OnSourceClick;
                _paragraph.Inlines.Add(link);
                position = source.Start + source.Length;
            }
        }
        if (position < text.Length) _paragraph.Inlines.Add(new Run(text.Substring(position)));
        _typeface ??= new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (text.Length > 0)
            _textWidth = Math.Max(_textWidth, new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                _typeface, FontSize, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace + 24);
    }

    private void OnSourceClick(object sender, RoutedEventArgs e)
    { e.Handled = true; SourceClicked?.Invoke((OutputSourceLink)((Hyperlink)sender).Tag); }

    public TextWrapping TextWrapping
    {
        get => _wrapping;
        set { _wrapping = value; UpdatePageWidth(); }
    }

    public new void AppendText(string text) { _owned.Append(text); SetBuffer(_owned); }
    public void Clear() => Text = "";

    private void UpdatePageWidth() => Document.PageWidth = _wrapping == TextWrapping.NoWrap
        ? Math.Max(Math.Max(100, ActualWidth - 24), _textWidth) : Math.Max(100, ActualWidth - 24);
}
