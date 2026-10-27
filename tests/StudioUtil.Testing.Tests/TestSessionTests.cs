using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class TestSessionTests
{
    [Test]
    [NotInParallel("StudioUtilFixture")]
    public async Task DiscoversRunsLogsAndCancelsRealTUnitTests()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "StudioUtil.slnx")))
            directory = Directory.GetParent(directory)?.FullName ?? throw new InvalidOperationException("Repository not found.");
        string project = Path.Combine(directory, "tests", "StudioUtil.Testing.Fixture", "StudioUtil.Testing.Fixture.csproj");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var session = new WorkerClient(Path.Combine(directory, "src", "StudioUtil.Worker", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "net11.0", "StudioUtil.Worker.dll"));
        var modules = await session.BuildAsync(project, Console.WriteLine, timeout.Token);
        Check(modules.Count == 1, "MTP test project was not recognized.");
        var module = modules[0];

        var discovered = new Dictionary<string, TestNodeUpdate>();
        await session.DiscoverAsync(module, u => { if (u.Uid != null) discovered[u.Uid] = u; }, Console.WriteLine, timeout.Token);
        foreach (var u in discovered.Values) Console.WriteLine($"Discovered: {u.DisplayName} ({u.NodeType}) {u.FilePath}:{u.LineStart}");
        Check(discovered.Values.Count(u => u.NodeType != "group") == 6, "Expected six cases, including both data rows.");
        Check(discovered.Values.All(u => !string.IsNullOrEmpty(u.FilePath) && u.LineStart > 0), "Source navigation metadata is missing.");
        var ids = discovered.Values.Where(u => !u.DisplayName!.Contains("Slow")).Select(u => u.Uid!).ToArray();
        var results = new Dictionary<string, TestTreeNode>();
        bool attached = false;
        await session.RunAsync(module, ids, (pid, token) =>
        {
            Check(pid > 0 && System.Diagnostics.Process.GetProcessById(pid) != null, "Debug callback received no live host.");
            attached = true;
            return Task.CompletedTask;
        }, u =>
        {
            Check(attached, "Tests started before the debugger callback.");
            if (u.Uid == null) return;
            if (!results.TryGetValue(u.Uid, out var test)) results[u.Uid] = test = new TestTreeNode(u.DisplayName!, module, u.Uid);
            test.Apply(u);
        }, Console.WriteLine, timeout.Token);
        Check(results.Values.Count(t => t.State == "passed") == 3, "Passing/data-driven results were not received.");
        Check(results.Values.Count(t => t.State is "failed" or "error") == 1, "Failure result was not received.");
        Check(results.Values.Count(t => t.State == "skipped") == 1, "Skipped result was not received.");
        Check(results.Values.Any(t => t.Output.Contains("fixture stdout")), "Standard output was lost.");
        Check(results.Values.Any(t => t.Output.Contains("fixture failure") && t.Output.Contains("fixture stderr")), "Failure details or stderr were lost.");
        string slow = discovered.Values.Single(u => u.DisplayName!.Contains("Slow")).Uid!;
        using var cancel = new CancellationTokenSource();
        int hostPid = 0;
        try
        {
            await session.RunAsync(module, new[] { slow }, (pid, token) => { hostPid = pid; return Task.CompletedTask; }, u =>
            {
                if (u.ExecutionState == "in-progress") cancel.CancelAfter(200);
            }, Console.WriteLine, cancel.Token).WaitAsync(TimeSpan.FromSeconds(20));
            throw new InvalidOperationException("Cancellation was ignored.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        await Task.Delay(200);
        try
        {
            using var host = System.Diagnostics.Process.GetProcessById(hostPid);
            Check(host.HasExited, "Cancellation leaked a test host.");
        }
        catch (ArgumentException) { }
    }

    [Test]
    public void GutterMarkersFollowTestDeclarationsAndEdits()
    {
        const string text = "namespace Example;\nclass Suite {\n[Test]\n[Arguments(1)]\npublic void Runs(int value) { }\npublic void Helper() { }\n// [Test] public void Commented() {}\n[TUnit.Core.TestAttribute] public void Other() {}\n}";
        var markers = TestSourceDiscovery.Discover("tests.cs", text, default);
        Check(markers.Count == 2, "Helpers/comments or data attributes generated extra markers.");
        Check(markers[0].Position == text.IndexOf("Runs", StringComparison.Ordinal), "Marker must sit on the declaration, not its attributes.");
        Check(markers[0].Target.FirstLine == 3 && markers[0].Target.LastLine == 5, "Runtime selection must include the attribute source location.");
        Check(markers[1].Target.Name == "Other", "Qualified test attribute was missed.");
        var edited = TestSourceDiscovery.Discover("tests.cs", "\n\n" + text, default);
        Check(edited[0].Position == markers[0].Position + 2 && edited[0].Target.FirstLine == 5, "Marker did not move with edits.");
        Check(TestSourceDiscovery.Discover("tests.cs", "class Empty {}", default).Count == 0, "Removed tests left markers behind.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
