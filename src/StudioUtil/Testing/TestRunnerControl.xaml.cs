using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using EnvDTE;
using ToolkitThreadHelper = Community.VisualStudio.Toolkit.ToolkitThreadHelper;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace StudioUtil.Testing;

public partial class TestRunnerControl : UserControl, IDisposable
{
    private readonly ObservableCollection<TestTreeNode> _roots = new();
    private readonly Dictionary<string, TestTreeNode> _cases = new(StringComparer.Ordinal);
    private readonly HashSet<TestTreeNode> _runningTests = new();
    private readonly BlockingCollection<PendingTestUpdate> _pending = new(256);
    private readonly PendingLogBuffer _pendingLogs = new();
    private int _shownTotal = -1, _shownPassed = -1, _shownFailed = -1, _shownSkipped = -1;
    private long _shownSeconds = -1;
    private readonly WorkerClient _worker = WorkerClient.Shared;
    private readonly ToolkitThreadHelper _tasks = ToolkitThreadHelper.Create();
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _searchTimer;
    private readonly System.Diagnostics.Stopwatch _elapsed = new();
    private readonly Dictionary<TestTreeNode, bool> _expansionBeforeSearch = new();
    private string _phase = "Ready";
    private string _phaseDetail = "";
    private TestTreeNode? _displayedTest;
    private bool _outputDirty;
    private CancellationTokenSource? _operation;
    private DTE? _dte;
    private SolutionEvents? _solutionEvents;
    private bool _initialized;
    private bool _disposed;
    private bool _clearOnCompletion;
    private int _errors;
    
    private string? _solutionPath;
    private JoinableTask? _cacheWrites;
    private int _solutionGeneration;
    private Task? _initialization;
    private TestTreeNode? _contextTest;

    public TestRunnerControl()
    {
        InitializeComponent();
        TestOutput.SourceClicked += NavigateToOutputSource;
        SessionLog.SourceClicked += NavigateToOutputSource;
        var menu = new ContextMenu { Style = (Style)Resources["TestGutterMenu"] };
        menu.Items.Add(CreateTestAction("_Run", false, "\uE768"));
        menu.Items.Add(CreateTestAction("_Debug", true, "\uEBE8"));
        var copy = new MenuItem { Header = "_Copy", Style = (Style)Resources["TestGutterAction"],
            Icon = new TextBlock { Text = "\uE8C8", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 14 } };
        copy.Click += (_, e) =>
        {
            e.Handled = true;
            if (_contextTest == null) return;
            try { Clipboard.SetText(_contextTest.Name); }
            catch (System.Runtime.InteropServices.COMException ex) { Log($"Could not copy test name: {ex.Message}"); Drain(); }
        };
        menu.Items.Add(copy);
        Tests.ContextMenu = menu;
        Tests.ItemsSource = _roots;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => Drain(), Dispatcher);
        _timer.Stop();
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplySearch(); };
        UpdateChrome();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RunEventAsync(async () =>
    {
        if (_initialized) return;
        _initialized = true;
        try { await InitializeAsync(); UpdateChrome(); }
        catch (Exception ex) { Log(ex.Message); Drain(); }
    });

    private void RunEventAsync(Func<Task> action) => _tasks.JoinableTaskFactory.RunAsync(async () =>
    {
        try { await action(); }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ActivityLog.LogError("StudioUtil Tests", ex.ToString());
            if (!_disposed) { Log(ex.Message); Drain(); }
        }
    }).FileAndForget("StudioUtil/Tests/Event");

    internal void StartSourceRun(TestSourceTarget target, bool debug) => RunEventAsync(() => RunSourceAsync(target, debug));

    internal async Task DiscoverOnOpenAsync()
    {
        await InitializeAsync();
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (_roots.Count == 0 && _dte?.Solution.IsOpen == true) await DiscoverAsync();
    }

    private Task InitializeAsync() => _initialization ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (_dte != null) return;
        var service = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(DTE));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _dte = service as DTE ?? throw new InvalidOperationException("Visual Studio automation is unavailable.");
        if (_dte != null && _solutionEvents == null)
        {
            _solutionEvents = _dte.Events.SolutionEvents;
            _solutionEvents.AfterClosing += OnSolutionClosed;
            _solutionEvents.Opened += OnSolutionOpened;
            await RestoreSessionAsync();
        }
    }

    private void OnSolutionClosed()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        QueueSaveSession();
        _solutionGeneration++;
        _solutionPath = null;
        _operation?.Cancel();
        _clearOnCompletion = _operation != null;
        Clear();
        SessionLog.Clear();
        Status.Text = "Open a solution to discover TUnit/MTP tests.";
    }

    private void OnSolutionOpened() => RunEventAsync(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        Clear();
        if (_operation == null) await RestoreSessionAsync();
    });

    private void Clear()
    {
        _roots.Clear(); _cases.Clear(); _runningTests.Clear(); TestOutput.Clear();
        _expansionBeforeSearch.Clear();
        Tests.ItemsSource = _roots;
        ShowOutput(null);
        UpdateChrome();
    }

    private Task DiscoverAsync() => ExecuteAsync(async token =>
    {
        await InitializeAsync();
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        if (_dte == null || !_dte.Solution.IsOpen) throw new InvalidOperationException("Open a solution first.");
        _dte.ExecuteCommand("File.SaveAll");
        var projects = TestSolution.Projects(_dte);
        await DiscoverCoreAsync(projects, token);
        token.ThrowIfCancellationRequested();
        RemoveTests(t => !projects.Contains(t.Module.ProjectPath, StringComparer.OrdinalIgnoreCase));
    });

    private async Task DiscoverCoreAsync(IReadOnlyList<string> projects, CancellationToken token)
    {
        foreach (string project in projects)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                SetPhase("Building", Path.GetFileNameWithoutExtension(project));
                var modules = await _worker.BuildAsync(project, Log, token);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var module in modules)
                {
                    SetPhase("Discovering", module.DisplayName);
                    await _worker.DiscoverAsync(module, update =>
                    {
                        if (string.IsNullOrEmpty(update.Uid) || update.NodeType == "group") return;
                        seen.Add(module.Key + "|" + update.Uid);
                        QueueUpdate(module, update, true, token);
                    }, Log, token);
                }
                token.ThrowIfCancellationRequested();
                await FlushUpdatesAsync();
                token.ThrowIfCancellationRequested();
                RemoveTests(t => t.Module.ProjectPath.Equals(project, StringComparison.OrdinalIgnoreCase) && !seen.Contains(t.Module.Key + "|" + t.Uid));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _errors++;
                ActivityLog.LogError("StudioUtil Tests", ex.ToString());
                Log($"Discovery failed for {project}:\n{ex}");
            }
        }
        if (_cases.Count == 0 && _errors == 0) Log("No tests discovered. Projects must opt into Microsoft.Testing.Platform (IsTestingPlatformApplication=true).");
    }

    internal async Task RunSourceAsync(TestSourceTarget target, bool debug)
    {
        await ExecuteAsync(async token =>
        {
            await InitializeAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            var project = _dte?.Solution.FindProjectItem(target.File)?.ContainingProject;
            if (project == null) throw new InvalidOperationException("The source file is not part of a loaded project.");
            _dte!.ExecuteCommand("File.SaveAll");
            string path = project.FullName;
            await DiscoverCoreAsync(new[] { path }, token);
            if (_errors > 0) return;
            var selected = _cases.Values.Where(t => t.Module.ProjectPath.Equals(path, StringComparison.OrdinalIgnoreCase)
                && SameFile(t.FilePath, target.File) && t.Line >= target.FirstLine && t.Line <= target.LastLine).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException($"No runtime tests map to {target.Name}. Refresh discovery and check the session log and test source locations.");
            Search.Clear();
            _searchTimer.Stop();
            ApplySearch();
            SelectTest(Tests, selected[0]);
            await RunCoreAsync(selected, debug, false, false, token);
        });
    }

    private Task RunAsync(bool all, bool debug, TestTreeNode? target = null)
    {
        var selected = all ? _cases.Values.ToArray() : Descendants(target ?? Tests.SelectedItem as TestTreeNode).ToArray();
        return ExecuteAsync(async token =>
        {
            await InitializeAsync();
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
            if (selected.Length == 0) throw new InvalidOperationException("Select tests to run, or use Refresh to discover tests first.");
            _dte?.ExecuteCommand("File.SaveAll");
            await RunCoreAsync(selected, debug, all, true, token);
        });
    }

    private async Task RunCoreAsync(TestTreeNode[] selected, bool debug, bool all, bool build, CancellationToken token)
    {
        foreach (var group in selected.GroupBy(t => t.Module.Key))
        {
            token.ThrowIfCancellationRequested();
            var module = group.First().Module;
            try
            {
                if (build)
                {
                    SetPhase("Building", module.DisplayName);
                    await _worker.BuildModuleAsync(module, Log, token);
                }
                SetPhase(debug ? "Debugging" : "Running", $"{group.Count()} test(s) in {module.DisplayName}");
                foreach (var test in group) test.Reset();
                Log($"{(debug ? "Debugging" : "Running")} {module.DisplayName}…");
                Func<int, CancellationToken, Task>? attach = debug ? AttachDebuggerAsync : null;
                await _worker.RunAsync(module, all ? null : group.Select(t => t.Uid!).ToArray(), attach,
                    u => QueueUpdate(module, u, false, token), Log, token);
                await FlushUpdatesAsync();
                foreach (var test in group.Where(t => t.State is "queued" or "in-progress"))
                {
                    test.State = "not-run";
                    _errors++;
                    Log($"No final result for {test.Name}. The test may have changed; refresh discovery before retrying.");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _errors++;
                Log($"Run failed for {module.DisplayName}: {ex.Message}");
                await FlushUpdatesAsync();
                foreach (var test in group.Where(t => t.State is "queued" or "in-progress")) test.State = "error";
            }
        }
    }

    private async Task AttachDebuggerAsync(int pid, CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        if (_dte == null) throw new InvalidOperationException("Visual Studio's debugger is unavailable.");
        foreach (EnvDTE.Process process in _dte.Debugger.LocalProcesses)
        {
            if (process.ProcessID != pid) continue;
            process.Attach();
            Log($"Debugger attached to test host {pid}.");
            return;
        }
        throw new InvalidOperationException($"Test host {pid} exited before the debugger could attach.");
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (_disposed) return;
        if (_operation != null) { Log("An operation is already running. Stop it before starting another."); return; }
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _errors = 0;
        _elapsed.Restart();
        _phase = "Preparing";
        _phaseDetail = "";
        SetBusy(true);
        SessionLog.Clear();
        _timer.Start();
        bool cancelled = false;
        try { await action(operation.Token); }
        catch (OperationCanceledException)
        {
            cancelled = true;
            Log("Cancelled.");
            await FlushUpdatesAsync();
            foreach (var test in _cases.Values.Where(t => t.State is "queued" or "in-progress")) test.State = "cancelled";
        }
        catch (Exception ex) { _errors++; Log(ex.Message); ActivityLog.LogError("StudioUtil Tests", ex.ToString()); }
        finally
        {
            await FlushUpdatesAsync();
            _timer.Stop();
            _elapsed.Stop();
            _operation = null;
            SetBusy(false);
            if (_clearOnCompletion)
            {
                Clear(); _clearOnCompletion = false;
                if (!_disposed) await RestoreSessionAsync();
            }
            else
            {
                ApplySearch();
                if (_errors > 0) ShowSessionLog.IsChecked = true;
                ShowOutput(Tests.SelectedItem as TestTreeNode);
                Status.Text = $"{(cancelled ? "Cancelled. " : "")}{_cases.Count} tests • {_cases.Values.Count(t => t.State == "passed")} passed • {_cases.Values.Count(IsFailed)} failed • {_cases.Values.Count(t => t.State == "skipped")} skipped"
                    + (_errors > 0 ? $" • {_errors} runner error(s); see Session log" : "");
                ActivityLog.LogInformation("StudioUtil Tests", Status.Text);
                _phase = cancelled ? "Stopped" : _errors > 0 ? "Runner error — see session log" : "Ready";
                _phaseDetail = "";
                foreach (var root in _roots) root.RefreshGroup();
                UpdateChrome();
                if (!_disposed) { QueueSaveSession(); if (_cacheWrites != null) await _cacheWrites.JoinAsync(); }
            }
        }
    }

    private void QueueUpdate(TestModule module, TestNodeUpdate update, bool discovery, CancellationToken token)
    {
        try { _pending.Add(new PendingTestUpdate(module, update, discovery), token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ApplyUpdate(TestModule module, TestNodeUpdate update, bool discovery = false)
    {
        if (_disposed || _clearOnCompletion) return;
        if (string.IsNullOrEmpty(update.Uid) || update.NodeType == "group") return;
        string key = module.Key + "|" + update.Uid;
        if (!_cases.TryGetValue(key, out var test))
        {
            test = new TestTreeNode(update.DisplayName ?? update.Uid!, module, update.Uid);
            _cases.Add(key, test);
            var root = _roots.FirstOrDefault(n => n.Module.Key == module.Key);
            if (root == null) { root = new TestTreeNode(module.DisplayName, module) { IsExpanded = true }; _roots.Add(root); }
            string className = !string.IsNullOrEmpty(update.ClassName) ? update.ClassName!
                : string.IsNullOrEmpty(update.FilePath) ? "Tests" : Path.GetFileName(update.FilePath);
            var classNode = root.Children.FirstOrDefault(n => n.Name == className);
            if (classNode == null) { classNode = new TestTreeNode(className, module); InsertSorted(root.Children, classNode); }
            InsertSorted(classNode.Children, test);
        }
        test.UpdateModule(module);
        test.Apply(update, discovery);
        if (test.State == "in-progress") _runningTests.Add(test);
        else _runningTests.Remove(test);
        if (ReferenceEquals(Tests.SelectedItem, test)) _outputDirty = true;
    }

    private static void InsertSorted(ObservableCollection<TestTreeNode> items, TestTreeNode node)
    {
        int index = 0;
        while (index < items.Count && StringComparer.OrdinalIgnoreCase.Compare(items[index].DisplayName, node.DisplayName) <= 0) index++;
        items.Insert(index, node);
    }

    private void Log(string message) => _pendingLogs.Append(message + Environment.NewLine);

    private void RemoveTests(Func<TestTreeNode, bool> remove)
    {
        foreach (var root in _roots.ToArray())
        {
            foreach (var group in root.Children.ToArray())
            {
                foreach (var test in group.Children.Where(remove).ToArray())
                {
                    _cases.Remove(test.Module.Key + "|" + test.Uid);
                    group.Children.Remove(test);
                }
                if (group.Children.Count == 0) root.Children.Remove(group);
            }
            if (root.Children.Count == 0) _roots.Remove(root);
            else root.RefreshGroup();
        }
        ApplySearch();
        ShowOutput(Tests.SelectedItem as TestTreeNode);
    }

    private async Task RestoreSessionAsync()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        _phase = "Ready";
        _errors = 0;
        SessionLog.Clear();
        _solutionPath = _dte?.Solution.IsOpen == true ? _dte.Solution.FullName : null;
        if (string.IsNullOrEmpty(_solutionPath)) return;
        try
        {
            string solution = _solutionPath!;
            int generation = ++_solutionGeneration;
            if (_cacheWrites != null) await _cacheWrites.JoinAsync();
            var snapshot = await _worker.LoadAsync(solution, CancellationToken.None);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_disposed || generation != _solutionGeneration || solution != _solutionPath || _roots.Count > 0) return;
            if (snapshot == null) return;
            // Validate the entire snapshot before changing the visible tree.
            var roots = snapshot.Roots.Select(TestTreeNode.Restore).ToArray();
            var cases = roots.SelectMany(Descendants).ToDictionary(t => t.Module.Key + "|" + t.Uid, StringComparer.Ordinal);
            foreach (var root in roots) _roots.Add(root);
            foreach (var pair in cases) _cases.Add(pair.Key, pair.Value);
            SessionLog.Text = snapshot.SessionLog;
            _phase = "Previous results · " + snapshot.SavedUtc.ToLocalTime().ToString("g");
            if (snapshot.SelectedKey != null && _cases.TryGetValue(snapshot.SelectedKey, out var selected))
            {
                await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Loaded);
                if (!_disposed && generation == _solutionGeneration && _cases.TryGetValue(snapshot.SelectedKey, out var current) && ReferenceEquals(current, selected))
                {
                    SelectTest(Tests, selected);
                }
            }
            ApplySearch();
        }
        catch (Exception ex) { ActivityLog.LogWarning("StudioUtil Tests", "Could not restore test cache: " + ex.Message); }
    }

    private void QueueSaveSession()
    {
        if (string.IsNullOrEmpty(_solutionPath)) return;
        try
        {
            int budget = 4 * 1024 * 1024;
            var snapshot = new TestSessionSnapshot
            {
                SavedUtc = DateTime.UtcNow,
                SessionLog = SessionLog.Text.Length > 256 * 1024 ? SessionLog.Text.Substring(SessionLog.Text.Length - 256 * 1024) : SessionLog.Text,
                SelectedKey = Tests.SelectedItem is TestTreeNode { IsTest: true } selected ? selected.Module.Key + "|" + selected.Uid : null
            };
            foreach (var root in _roots) snapshot.Roots.Add(root.Capture(ref budget));
            string solution = _solutionPath!;
            var previous = _cacheWrites;
            _cacheWrites = _tasks.JoinableTaskFactory.RunAsync(() => SaveAfterAsync(previous, solution, snapshot));
            _cacheWrites.FileAndForget("StudioUtil/Tests/SaveSession");
        }
        catch (Exception ex) { ActivityLog.LogWarning("StudioUtil Tests", "Could not save test cache: " + ex.Message); }
    }

    private async Task SaveAfterAsync(JoinableTask? previous, string solution, TestSessionSnapshot snapshot)
    {
        try
        {
            if (previous != null) await previous.JoinAsync();
            await _worker.SaveAsync(solution, snapshot);
        }
        catch (Exception ex) { ActivityLog.LogWarning("StudioUtil Tests", "Could not save test cache: " + ex.Message); }
    }
    private void Drain()
    {
        // Large builds and discovery batches must yield to input and layout between updates.
        long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency / 125;
        for (int count = 0; count < 64 && _pending.TryTake(out var item); count++)
        {
            ApplyUpdate(item.Module, item.Update, item.Discovery);
            if (System.Diagnostics.Stopwatch.GetTimestamp() >= deadline) break;
        }
        _runningTests.RemoveWhere(static test => !test.RefreshRunningDuration());
        if (Tests.SelectedItem is TestTreeNode { State: "in-progress" } running)
            OutputDuration.Text = running.DurationLabel;
        foreach (var root in _roots) root.RefreshGroup();
        if (_outputDirty) { _outputDirty = false; ShowOutput(Tests.SelectedItem as TestTreeNode); }
        string text = _pendingLogs.Drain();
        if (text.Length > 0)
        {
            bool follow = SessionLog.VerticalOffset >= SessionLog.ExtentHeight - SessionLog.ViewportHeight - 2;
            SessionLog.AppendText(text);
            if (follow && SessionLog.IsVisible) SessionLog.ScrollToEnd();
        }
        UpdateChrome();
    }

    private async Task FlushUpdatesAsync()
    {
        while (_pending.Count > 0 || !_pendingLogs.IsEmpty)
        {
            Drain();
            await System.Windows.Threading.Dispatcher.Yield(DispatcherPriority.Background);
        }
    }

    private void SetBusy(bool busy)
    {
        DiscoverButton.IsEnabled = !busy;
        StopButton.IsEnabled = busy;
        UpdateChrome();
    }

    private void SetPhase(string phase, string detail)
    {
        _phase = phase; _phaseDetail = detail;
        UpdateChrome();
    }

    private void UpdateChrome()
    {
        bool busy = _operation != null;
        RunAllButton.IsEnabled = !busy && _cases.Count > 0;
        RunButton.IsEnabled = DebugButton.IsEnabled = !busy && Tests.SelectedItem is TestTreeNode { IsVisible: true } selected && selected.TestCount > 0;
        BusyProgress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        int passed = 0, failed = 0, skipped = 0;
        foreach (var root in _roots) { root.RefreshGroup(); passed += root.PassedCount; failed += root.FailedCount; skipped += root.SkippedCount; }
        if (_shownTotal != _cases.Count) { _shownTotal = _cases.Count; TotalCount.Text = $"{_shownTotal} tests"; }
        if (_shownPassed != passed) { _shownPassed = passed; PassedCount.Text = passed.ToString(); }
        if (_shownFailed != failed) { _shownFailed = failed; FailedCount.Text = failed.ToString(); }
        if (_shownSkipped != skipped) { _shownSkipped = skipped; SkippedCount.Text = skipped.ToString(); }
        string status = busy ? _phase + " · " + _phaseDetail : _phase;
        if (Status.Text != status) Status.Text = status;
        long seconds = (long)_elapsed.Elapsed.TotalSeconds;
        if (_shownSeconds != seconds) { _shownSeconds = seconds; Elapsed.Text = seconds > 0 ? _elapsed.Elapsed.ToString(@"m\:ss") : ""; }
        bool noMatches = _cases.Count > 0 && !_roots.Any(n => n.IsVisible);
        TreeEmpty.Visibility = _cases.Count == 0 || noMatches ? Visibility.Visible : Visibility.Collapsed;
        TreeEmptyTitle.Text = noMatches ? "No matching tests" : busy ? _phase + "…" : _errors > 0 ? "Discovery couldn't finish" : "Your tests, in one place";
        TreeEmptyDetail.Text = noMatches ? "Try another name or clear the search." : busy ? "Tests will appear here as they're discovered." : _errors > 0 ? "Open the session log for details, then refresh." : "Open a solution and select Refresh to discover its tests.";
        ClearSearchButton.Visibility = Search.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        CopyOutputButton.IsEnabled = (SessionLog.Visibility == Visibility.Visible ? SessionLog.TextLength : TestOutput.TextLength) > 0;
    }

    private static bool IsFailed(TestTreeNode test) => test.State is "failed" or "error" or "timeout";
    private static bool SameFile(string? left, string right) => !string.IsNullOrEmpty(left) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<TestTreeNode> Descendants(TestTreeNode? node) => node == null ? Enumerable.Empty<TestTreeNode>() : node.IsTest ? new[] { node } : node.Children.SelectMany(Descendants);

    private void ApplySearch()
    {
        string query = Search.Text.Trim();
        foreach (var root in _roots) Filter(root, query, false);
        if (query.Length == 0)
        {
            foreach (var entry in _expansionBeforeSearch) entry.Key.IsExpanded = entry.Value;
            _expansionBeforeSearch.Clear();
        }
        UpdateChrome();
    }

    private bool Filter(TestTreeNode node, string query, bool ancestorMatches)
    {
        bool matches = query.Length == 0 || ancestorMatches || node.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
            || (node.FilePath ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        bool childrenMatch = false;
        foreach (var child in node.Children) childrenMatch |= Filter(child, query, matches);
        node.IsVisible = matches || childrenMatch;
        if (query.Length > 0 && !node.IsTest)
        {
            if (!_expansionBeforeSearch.ContainsKey(node)) _expansionBeforeSearch.Add(node, node.IsExpanded);
            node.IsExpanded = node.IsVisible;
        }
        return node.IsVisible;
    }

    private void ShowOutput(TestTreeNode? test)
    {
        bool session = ShowSessionLog.IsChecked == true || test?.IsTest != true;
        ResultBanner.DataContext = session ? null : test;
        SessionLog.Visibility = session ? Visibility.Visible : Visibility.Collapsed;
        TestOutput.Visibility = session ? Visibility.Collapsed : Visibility.Visible;
        OutputTitle.Text = session ? "Session log" : test!.Name;
        OutputTitle.ToolTip = OutputTitle.Text;
        OutputState.Text = session ? "Build & runner" : test!.StateLabel + (test.LastRunUtc.HasValue ? " · " + test.LastRunUtc.Value.ToLocalTime().ToString("g") : "");
        OutputDuration.Text = session ? "" : test!.DurationLabel;
        SourceButton.Visibility = !session && !string.IsNullOrEmpty(test!.FilePath) ? Visibility.Visible : Visibility.Collapsed;
        SourceButton.Content = session ? "" : $"{Path.GetFileName(test!.FilePath)}:{test.Line}";
        SourceButton.ToolTip = test?.FilePath;
        OutputEmpty.Visibility = !session && test!.OutputBuffer.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        OutputEmpty.Text = test?.State == "in-progress" ? "Waiting for test output…" : test?.State is "discovered" or "queued" ? "Run this test to see its output." : "This test did not write any output.";
        if (session) { _displayedTest = null; return; }
        bool changedSelection = !ReferenceEquals(_displayedTest, test);
        _displayedTest = test;
        bool followOutput = TestOutput.VerticalOffset >= TestOutput.ExtentHeight - TestOutput.ViewportHeight - 1;
        TestOutput.SetBuffer(test!.OutputBuffer);
        if (changedSelection) { TestOutput.ScrollToHome(); TestOutput.ScrollToHorizontalOffset(0); }
        else if (followOutput) TestOutput.ScrollToEnd();
    }

    private void OnLogViewChanged(object sender, RoutedEventArgs e) { ShowOutput(Tests.SelectedItem as TestTreeNode); UpdateChrome(); }
    private static bool SelectTest(ItemsControl parent, TestTreeNode test)
    {
        parent.UpdateLayout();
        for (int index = 0; index < parent.Items.Count; index++)
        {
            var item = parent.Items[index];
            if (item is not TestTreeNode node) continue;
            var ancestor = test;
            while (ancestor != null && !ReferenceEquals(ancestor, node)) ancestor = ancestor.Parent;
            if (ancestor == null) continue;
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem)
            {
                FindItemsPanel(parent)?.ShowIndex(index);
                parent.UpdateLayout();
            }
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, test))
            {
                container.IsSelected = true;
                container.BringIntoView(new Rect(0, 0, 1, container.ActualHeight));
                return true;
            }
            container.IsExpanded = true;
            if (SelectTest(container, test)) return true;
        }
        return false;
    }

    private static TestItemsPanel? FindItemsPanel(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TestItemsPanel panel) return panel;
            // Only find this control's items host, not a child item's panel.
            if (child is TreeViewItem) continue;
            var found = FindItemsPanel(child);
            if (found != null) return found;
        }
        return null;
    }
    private void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TestTreeNode { IsTest: true }) ShowSessionLog.IsChecked = false;
        ShowOutput(e.NewValue as TestTreeNode);
        UpdateChrome();
    }
    private void OnSearch(object sender, TextChangedEventArgs e) { if (_searchTimer != null) { _searchTimer.Stop(); _searchTimer.Start(); } }
    private void OnClearSearch(object sender, RoutedEventArgs e) { Search.Clear(); Search.Focus(); ApplySearch(); }
    private void OnCopyOutput(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(SessionLog.Visibility == Visibility.Visible ? SessionLog.Text : TestOutput.Text); }
        catch (System.Runtime.InteropServices.COMException ex) { Log($"Could not copy output: {ex.Message}"); Drain(); }
    }
    private void OnWrapOutput(object sender, RoutedEventArgs e)
    {
        if (TestOutput == null || SessionLog == null) return;
        TestOutput.TextWrapping = SessionLog.TextWrapping = WrapOutput.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }
    private void OnOpenSource(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        NavigateToSelectedTest();
    }
    private void OnDiscover(object sender, RoutedEventArgs e) => RunEventAsync(DiscoverAsync);
    private void OnRunAll(object sender, RoutedEventArgs e) => RunEventAsync(() => RunAsync(true, false));
    private void OnRun(object sender, RoutedEventArgs e) => RunEventAsync(() => RunAsync(false, false));
    private void OnDebug(object sender, RoutedEventArgs e) => RunEventAsync(() => RunAsync(false, true));
    private void OnStop(object sender, RoutedEventArgs e) { _operation?.Cancel(); SetPhase("Stopping", "Waiting for the test host to exit"); StopButton.IsEnabled = false; }

    private MenuItem CreateTestAction(string title, bool debug, string glyph)
    {
        var action = new MenuItem { Header = title, Style = (Style)Resources["TestGutterAction"],
            Icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 14 } };
        action.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(IsEnabled)) { Source = debug ? DebugButton : RunButton });
        action.Click += (_, e) =>
        {
            e.Handled = true;
            var target = _contextTest;
            if (target != null && _operation == null) RunEventAsync(() => RunAsync(false, debug, target));
        };
        return action;
    }

    private void OnTestRightClick(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        _contextTest = row?.DataContext as TestTreeNode;
        if (row == null) return;
        row.IsSelected = true;
        row.Focus();
    }

    private void OnTestMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Shift+F10 and the menu key act on the keyboard selection; blank space has no target.
        if (e.CursorLeft < 0) _contextTest = Tests.SelectedItem as TestTreeNode;
        if (_contextTest == null || !_contextTest.IsVisible || !Descendants(_contextTest).Any()) e.Handled = true;
    }

    private void NavigateToOutputSource(OutputSourceLink source) => RunEventAsync(() => NavigateToOutputSourceAsync(source));

    private async Task NavigateToOutputSourceAsync(OutputSourceLink source)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        try
        {
            string path = source.Path;
            if (!Path.IsPathRooted(path))
            {
                string? directory = SessionLog.Visibility == Visibility.Visible ? Path.GetDirectoryName(_solutionPath)
                    : Path.GetDirectoryName(_displayedTest?.Module.ProjectPath);
                if (string.IsNullOrEmpty(directory)) throw new FileNotFoundException("Cannot resolve the relative source path.", path);
                path = Path.GetFullPath(Path.Combine(directory, path));
            }
            if (await _worker.PathKindAsync(path) != "file") throw new FileNotFoundException("Source file is not available on this machine.", path);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var window = _dte?.ItemOperations.OpenFile(path);
            if (window?.Document?.Selection is TextSelection selection)
            {
                selection.MoveToLineAndOffset(source.Line, source.Column);
                window.Activate();
            }
        }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ActivityLog.LogWarning("StudioUtil Tests", ex.ToString());
            MessageBox.Show(ex.Message, "Open test source", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnNavigate(object sender, MouseButtonEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (e.OriginalSource is DependencyObject source && FindAncestor<TreeViewItem>(source)?.DataContext is TestTreeNode { IsTest: true }) NavigateToSelectedTest();
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T found) return found;
            source = source is FrameworkContentElement content ? content.Parent : System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return null;
    }

    private void NavigateToSelectedTest()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (Tests.SelectedItem is not TestTreeNode test || string.IsNullOrEmpty(test.FilePath)) return;
        NavigateToOutputSource(new OutputSourceLink { Path = test.FilePath!, Line = Math.Max(1, test.Line) });
    }

    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_disposed) return;
        QueueSaveSession();
        _disposed = true;
        _operation?.Cancel();
        _timer.Stop();
        _searchTimer.Stop();
        if (_solutionEvents != null)
        {
            _solutionEvents.AfterClosing -= OnSolutionClosed;
            _solutionEvents.Opened -= OnSolutionOpened;
        }
        _tasks.Dispose();
    }
}

