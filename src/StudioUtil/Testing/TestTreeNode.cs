using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Text;


namespace StudioUtil.Testing;

internal sealed class TestTreeNode : INotifyPropertyChanged
{
    private string _state = "discovered";
    internal BoundedTextBuffer OutputBuffer { get; } = new();
    private static readonly ConcurrentDictionary<string, PropertyChangedEventArgs> ChangeArgs = new();
    private TestTreeNode? _parent;
    internal TestTreeNode? Parent => _parent;
    private bool _groupDirty = true;
    private int _passed, _failed, _skipped;
    private string _name;
    private double? _duration;
    private bool _expanded;
    private bool _visible = true;
    private int _count;

    public TestTreeNode(string name, TestModule module, string? uid = null)
    {
        _name = name;
        Module = module;
        Uid = uid;
        Children.CollectionChanged += OnChildrenChanged;
    }

    public string Name => _name;
    public string DisplayName => !IsTest && Name != Module.DisplayName ? Name.Substring(Name.LastIndexOf('.') + 1) : Name;
    public TestModule Module { get; private set; }
    public DateTime? LastRunUtc { get; private set; }
    public string? Uid { get; }
    public string? FilePath { get; private set; }
    public int Line { get; private set; }
    public bool IsTest => Uid != null;
    public bool IsExpanded { get => _expanded; set { if (_expanded == value) return; _expanded = value; Changed(); } }
    public bool IsVisible { get => _visible; set { if (_visible == value) return; _visible = value; Changed(); } }
    public int TestCount => IsTest ? 1 : _count;
    public string CountLabel => IsTest ? "" : $"{TestCount}";
    public ObservableCollection<TestTreeNode> Children { get; } = new();
    public string State { get => _state; set { if (_state == value) return; _state = value; _parent?.MarkDirty(); Changed(); Changed(nameof(Label)); Changed(nameof(Symbol)); Changed(nameof(StateLabel)); } }
    public string StateLabel => State switch { "" or "discovered" or "not-run" => "Not run", "in-progress" => "Running", _ => char.ToUpperInvariant(State[0]) + State.Substring(1) };
    public string Output { get => OutputBuffer.GetText(); private set { OutputBuffer.Clear(); OutputBuffer.Append(value); Changed(); } }
    public int PassedCount => IsTest ? (State == "passed" ? 1 : 0) : _passed;
    public int FailedCount => IsTest ? (State is "failed" or "error" or "timeout" ? 1 : 0) : _failed;
    public int SkippedCount => IsTest ? (State == "skipped" ? 1 : 0) : _skipped;
    public string Label => IsTest ? $"{Symbol}  {Name}{(_duration.HasValue ? $"  ({_duration:0.##} ms)" : "")}" : Name;
    public string DurationLabel => !_duration.HasValue ? "" : _duration.Value >= 1000 ? $"{_duration.Value / 1000:0.##} s" : $"{_duration.Value:0.##} ms";
    public string Symbol => State switch
    {
        "passed" => "✓", "failed" or "error" or "timeout" => "✗", "in-progress" => "▶",
        "skipped" => "↷", "cancelled" => "■", _ => IsTest ? "○" : "▣"
    };

    public void RefreshGroup()
    {
        if (IsTest || !_groupDirty) return;
        int count = 0, passed = 0, failed = 0, skipped = 0;
        bool running = false, cancelled = false, allPassed = Children.Count > 0, allSkipped = Children.Count > 0;
        foreach (var child in Children)
        {
            child.RefreshGroup();
            count += child.TestCount; passed += child.PassedCount; failed += child.FailedCount; skipped += child.SkippedCount;
            running |= child.State == "in-progress"; cancelled |= child.State == "cancelled";
            allPassed &= child.State is "passed" or "skipped"; allSkipped &= child.State == "skipped";
        }
        _passed = passed; _failed = failed; _skipped = skipped;
        if (_count != count) { _count = count; Changed(nameof(TestCount)); Changed(nameof(CountLabel)); }
        State = running ? "in-progress" : failed > 0 ? "failed" : allSkipped ? "skipped" : allPassed ? "passed" : cancelled ? "cancelled" : "discovered";
        _groupDirty = false;
    }

    private void MarkDirty() { if (_groupDirty) return; _groupDirty = true; _parent?.MarkDirty(); }
    private void OnChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (TestTreeNode child in e.OldItems) child._parent = null;
        if (e.NewItems != null) foreach (TestTreeNode child in e.NewItems) child._parent = this;
        MarkDirty();
    }

    public void Reset()
    {
        State = "queued";
        _duration = null;
        LastRunUtc = null;
        Output = "";
        Changed(nameof(Label));
        Changed(nameof(DurationLabel));
    }

    public void UpdateModule(TestModule module) => Module = module;

    public TestNodeSnapshot Capture(ref int outputBudget)
    {
        int length = Math.Min(Output.Length, Math.Min(64 * 1024, outputBudget));
        outputBudget -= length;
        var snapshot = new TestNodeSnapshot
        {
            Name = Name, ProjectPath = Module.ProjectPath, Framework = Module.Framework, AssemblyPath = Module.AssemblyPath,
            Uid = Uid, FilePath = FilePath, Line = Line, State = State is "queued" or "in-progress" ? "cancelled" : State,
            Output = length == Output.Length ? Output : "[Earlier output omitted from cache]\n" + Output.Substring(Output.Length - length),
            Duration = _duration, LastRunUtc = LastRunUtc, IsExpanded = IsExpanded
        };
        foreach (var child in Children) snapshot.Children.Add(child.Capture(ref outputBudget));
        return snapshot;
    }

    public static TestTreeNode Restore(TestNodeSnapshot snapshot)
    {
        var node = new TestTreeNode(snapshot.Name, new TestModule(snapshot.ProjectPath, snapshot.Framework, snapshot.AssemblyPath), snapshot.Uid)
        {
            FilePath = snapshot.FilePath, Line = snapshot.Line, State = snapshot.State,
            Output = snapshot.Output, _duration = snapshot.Duration, LastRunUtc = snapshot.LastRunUtc, IsExpanded = snapshot.IsExpanded
        };
        foreach (var child in snapshot.Children) node.Children.Add(Restore(child));
        node.RefreshGroup();
        return node;
    }

    public void Apply(TestNodeUpdate update, bool discovery = false)
    {
        bool nameChanged = update.DisplayName != null && update.DisplayName != _name;
        if (nameChanged) { _name = update.DisplayName!; Changed(nameof(Name)); Changed(nameof(DisplayName)); Changed(nameof(Label)); }
        FilePath = update.FilePath ?? FilePath;
        Line = update.LineStart ?? Line;
        if (!discovery)
        {
            if (update.DurationInMilliseconds.HasValue && update.DurationInMilliseconds != _duration)
            { _duration = update.DurationInMilliseconds; Changed(nameof(DurationLabel)); Changed(nameof(Label)); }
            State = update.ExecutionState ?? State;
            if (State is "passed" or "failed" or "error" or "timeout" or "skipped" or "cancelled") LastRunUtc = DateTime.UtcNow;
            var detail = Detail(update);
            if (detail is { Length: > 0 })
            {
                // Updates can contain a complete output snapshot; don't duplicate it when a later state repeats it.
                if (!OutputBuffer.EndsWith(detail))
                {
                    if (OutputBuffer.Length > 0) OutputBuffer.Append(Environment.NewLine);
                    OutputBuffer.Append(detail);
                    Changed(nameof(Output));
                }
            }
        }
    }

    private static string? Detail(TestNodeUpdate update)
    {
        StringBuilder? text = null;
        void Add(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (text == null) text = new StringBuilder(value);
            else text.AppendLine().Append(value);
        }
        Add(update.ErrorMessage); Add(update.ErrorStackTrace); Add(update.StandardOutput); Add(update.StandardError);
        return text?.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, ChangeArgs.GetOrAdd(name!, n => new PropertyChangedEventArgs(n)));
}
