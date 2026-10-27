using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace StudioUtil.Testing;

internal sealed class TestMargin : Canvas, IWpfTextViewMargin
{
    internal const string MarginName = "StudioUtil Test Gutter";
    private readonly IWpfTextView _view;
    private readonly ITextDocument _document;
    private readonly DispatcherTimer _debounce;
    private readonly ToolkitThreadHelper _tasks = ToolkitThreadHelper.Create();
    private CancellationTokenSource? _parsing;
    private ITextSnapshot? _snapshot;
    private IReadOnlyList<TestDeclaration> _declarations = Array.Empty<TestDeclaration>();
    private ContextMenu? _menu;
    private bool _disposed;

    public TestMargin(IWpfTextView view, ITextDocument document)
    {
        _view = view;
        _document = document;
        Width = 22;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/StudioUtil;component/Testing/TestGutterResources.xaml", UriKind.Relative)
        });
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += Parse;
        view.TextBuffer.Changed += OnBufferChanged;
        view.LayoutChanged += OnLayoutChanged;
        view.ZoomLevelChanged += OnZoomLevelChanged;
        view.Closed += OnClosed;
        Loaded += OnLoaded;
        _debounce.Start();
    }

    public FrameworkElement VisualElement => this;
    public double MarginSize => Width;
    public bool Enabled => !_disposed;
    public ITextViewMargin? GetTextViewMargin(string marginName) => string.Equals(marginName, MarginName, StringComparison.OrdinalIgnoreCase) ? this : null;

    private void OnBufferChanged(object sender, TextContentChangedEventArgs e)
    {
        _parsing?.Cancel();
        if (_menu != null) _menu.IsOpen = false;
        Children.Clear();
        _debounce.Stop();
        _debounce.Start();
    }

    private void Parse(object? sender, EventArgs e) =>
        _tasks.JoinableTaskFactory.RunAsync(ParseAsync).FileAndForget("StudioUtil/Tests/Parse");

    private async Task ParseAsync()
    {
        _debounce.Stop();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_tasks.DisposalToken);
        _parsing = cancellation;
        try
        {
            var snapshot = _view.TextSnapshot;
            string file = _document.FilePath;
            var declarations = await WorkerClient.Shared.ParseAsync(file, snapshot.GetText(), cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || snapshot != _view.TextSnapshot) return;
            _snapshot = snapshot;
            _declarations = declarations;
            Render();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ActivityLog.LogError("StudioUtil Tests", ex.ToString()); }
        finally { if (ReferenceEquals(_parsing, cancellation)) _parsing = null; }
    }

    private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e) => Render();
    private void OnZoomLevelChanged(object sender, ZoomLevelChangedEventArgs e) => Render();
    private void OnLoaded(object sender, RoutedEventArgs e) => Render();
    private void Render()
    {
        if (_disposed || !IsLoaded || _view.IsClosed || _snapshot != _view.TextSnapshot) return;
        // CodeLens and other editor adornments can relayout while the user chooses an action.
        // Keep the clicked button alive until the menu closes; edits already close it above.
        if (_menu?.IsOpen == true) return;
        Children.Clear();
        var lines = _view.TextViewLines;
        if (lines == null) return;
        // Text coordinates belong to the zoomed editor surface, not this margin.
        // Transform the text center before centering the fixed-size gutter button.
        var editorToMargin = _view.VisualElement.TransformToVisual(this);
        foreach (var declaration in _declarations)
        {
            var point = new SnapshotPoint(_snapshot, declaration.Position);
            if (!lines.ContainsBufferPosition(point)) continue;
            var line = lines.GetTextViewLineContainingBufferPosition(point);
            var button = CreateButton(declaration, _snapshot);
            SetLeft(button, 2);
            var center = editorToMargin.Transform(new Point(0, line.TextTop - _view.ViewportTop + line.TextHeight / 2));
            SetTop(button, center.Y - button.Height / 2);
            Children.Add(button);
        }
    }

    private Button CreateButton(TestDeclaration declaration, ITextSnapshot snapshot)
    {
        var indicator = new TextBlock { Text = "◐", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        indicator.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        var button = new Button
        {
            Width = 18, Height = 18, Padding = new Thickness(0), BorderThickness = new Thickness(0),
            Background = Brushes.Transparent, Content = indicator, ToolTip = $"Run or debug {declaration.Target.Name}",
            Cursor = System.Windows.Input.Cursors.Hand
        };
        button.Style = (Style)Resources["TestGutterButton"];
        AutomationProperties.SetName(button, $"Test actions for {declaration.Target.Name}");
        var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom };
        menu.Style = (Style)Resources["TestGutterMenu"];
        menu.SetResourceReference(Control.BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);
        menu.SetResourceReference(Control.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
        menu.Items.Add(CreateAction("_Run", false, declaration, snapshot));
        menu.Items.Add(CreateAction("_Debug", true, declaration, snapshot));
        menu.Closed += (_, _) => Render();
        button.ContextMenu = menu;
        button.Click += (_, args) =>
        {
            args.Handled = true;
            if (_menu != null) _menu.IsOpen = false;
            _menu = menu;
            menu.IsOpen = true;
        };
        return button;
    }

    private MenuItem CreateAction(string title, bool debug, TestDeclaration declaration, ITextSnapshot snapshot)
    {
        var item = new MenuItem { Header = title, Style = (Style)Resources["TestGutterAction"] };
        var icon = new Path { Width = 16, Height = 16, StrokeThickness = 1.3 };
        if (debug)
        {
            icon.Data = Geometry.Parse("M 5,6 L 11,6 L 11,11 A 3,3 0 0 1 5,11 Z M 6,6 L 6,3 L 10,3 L 10,6 M 6,3 L 4,1 M 10,3 L 12,1 M 5,7 L 2,5 M 11,7 L 14,5 M 5,10 L 1,10 M 11,10 L 15,10 M 5,12 L 2,15 M 11,12 L 14,15");
            icon.SetResourceReference(Shape.StrokeProperty, EnvironmentColors.ToolWindowTextBrushKey);
        }
        else
        {
            icon.Data = Geometry.Parse("M 2,1 L 13,8 L 2,15 Z");
            icon.Stroke = Brushes.LightGreen;
        }
        item.Icon = icon;
        item.Click += (_, args) =>
        {
            args.Handled = true;
            // A menu opened before an edit must never execute a different test at the old line numbers.
            if (_disposed || snapshot != _view.TextSnapshot) return;
            _tasks.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var runner = await TestRunnerHost.ShowAsync(_tasks.DisposalToken);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(_tasks.DisposalToken);
                    if (!_disposed) runner.StartSourceRun(declaration.Target, debug);
                }
                catch (Exception ex) { ActivityLog.LogError("StudioUtil Tests", ex.ToString()); }
            }).FileAndForget("StudioUtil/Tests/RunSource");
        };
        return item;
    }

    private void OnClosed(object sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounce.Stop();
        _debounce.Tick -= Parse;
        _parsing?.Cancel();
        _view.TextBuffer.Changed -= OnBufferChanged;
        _view.LayoutChanged -= OnLayoutChanged;
        _view.ZoomLevelChanged -= OnZoomLevelChanged;
        _view.Closed -= OnClosed;
        Loaded -= OnLoaded;
        if (_menu != null) _menu.IsOpen = false;
        Children.Clear();
        _tasks.Dispose();
    }
}
