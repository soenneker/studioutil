using System;
using System.Collections.Generic;
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
    private int _renderedCharacters;
    private int _renderedLines;
    private readonly Queue<(Paragraph Paragraph, int Length)> _completed = new();
    private Paragraph? _omission;
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
        IsVisibleChanged += (_, _) => { if (IsVisible) SetBuffer(_source ?? _owned); };
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
        if (!IsVisible) { _source = source; _version = -1; return; }
        if (ReferenceEquals(_source, source) && _version == source.Version) return;
        bool reset = _version < 0 || !ReferenceEquals(_source, source) || _generation != source.Generation || _removed != source.Removed || source.Length < _length;
        int delta = reset ? source.Length : source.Length - _length;
        reset |= delta > OutputDisplayWindow.MaxCharacters || _tail.Length + delta > OutputDisplayWindow.MaxCharacters;
        string added = source.GetText(reset ? Math.Max(0, source.Length - OutputDisplayWindow.MaxCharacters) : _length);
        int displayStart = OutputDisplayWindow.Start(added);
        if (displayStart > 0) { added = added.Substring(displayStart); reset = true; }
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
                _renderedCharacters = 0; _renderedLines = 0;
                _completed.Clear(); _omission = null;
                if (source.Removed > 0 || source.Length > added.Length)
                    ShowOmission();
            }
            _renderedCharacters += added.Length;
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
                RenderLine(pending.Substring(start, i - start).TrimEnd('\r'), true);
                _completed.Enqueue((_paragraph, i - start + 1));
                _paragraph = new Paragraph { Margin = new Thickness(0) };
                Document.Blocks.Add(_paragraph);
                _renderedLines++;
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
            while (_completed.Count > 0 && (_renderedLines > OutputDisplayWindow.MaxLines || _renderedCharacters > OutputDisplayWindow.MaxCharacters))
            {
                var old = _completed.Dequeue();
                Document.Blocks.Remove(old.Paragraph);
                _renderedCharacters -= old.Length;
                _renderedLines--;
                ShowOmission();
            }
            UpdatePageWidth();
        }
        finally { EndChange(); }
    }

    private void ShowOmission()
    {
        if (_omission != null) return;
        _omission = new Paragraph(new Run("Showing recent output · Copy includes the retained log"))
        { Margin = new Thickness(0, 0, 0, 6), FontStyle = FontStyles.Italic };
        Document.Blocks.InsertBefore(Document.Blocks.FirstBlock, _omission);
    }

    private void RenderLine(string text, bool parse)
    {
        int position = 0;
        if (parse && text.Length <= 4096)
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
        // Output uses a monospace font. Avoid shaping every line a second time just
        // to size the horizontal scrollbar; WPF already shapes it for display.
        int columns = 0;
        foreach (char character in text) columns += character == '\t' ? 8 : 1;
        _textWidth = Math.Max(_textWidth, columns * FontSize + 24);
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
