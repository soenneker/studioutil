using System;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class RunnerBufferTests
{
    [Test]
    public async Task CacheCaptureCopiesOnlyItsOutputBudget()
    {
        var test = new TestTreeNode("test", new TestModule("project.csproj", "net11.0", "test.dll"), "1");
        int warmupBudget = 0;
        test.Capture(ref warmupBudget);
        test.OutputBuffer.Append(new string('x', 900000));
        int budget = 1024;
        long before = GC.GetAllocatedBytesForCurrentThread();
        var snapshot = test.Capture(ref budget);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated < 20000).IsTrue();
        await Assert.That(snapshot.Output.EndsWith(new string('x', 1024), StringComparison.Ordinal)).IsTrue();
        await Assert.That(budget).IsEqualTo(0);
        await Assert.That(test.OutputBuffer.Length).IsEqualTo(900000);
    }

    [Test]
    public async Task DisplayWindowBoundsFloodsWithoutTruncatingRetainedOutput()
    {
        string flood = string.Concat(System.Linq.Enumerable.Repeat("log line\n", 10000));
        var retained = new BoundedTextBuffer();
        retained.Append(flood);
        string shown = flood.Substring(OutputDisplayWindow.Start(flood));
        await Assert.That(shown.Length <= OutputDisplayWindow.MaxCharacters).IsTrue();
        await Assert.That(System.Linq.Enumerable.Count(shown, c => c == '\n') <= OutputDisplayWindow.MaxLines).IsTrue();
        await Assert.That(retained.GetText()).IsEqualTo(flood);
        string longLine = new('x', 100000);
        await Assert.That(longLine.Length - OutputDisplayWindow.Start(longLine)).IsEqualTo(OutputDisplayWindow.MaxCharacters);
    }

    [Test]
    public async Task RunningDurationTicksThenKeepsTheReportedResult()
    {
        var test = new TestTreeNode("test", new TestModule("project.csproj", "net11.0", "test.dll"), "1");
        test.State = "in-progress";
        await Task.Delay(30);
        await Assert.That(test.RefreshRunningDuration()).IsTrue();
        int budget = 0;
        await Assert.That(test.Capture(ref budget).Duration > 0).IsTrue();
        test.Apply(new TestNodeUpdate { ExecutionState = "passed", DurationInMilliseconds = 1234 });
        await Assert.That(test.RefreshRunningDuration()).IsFalse();
        await Assert.That(test.Capture(ref budget).Duration).IsEqualTo(1234);
        test.Reset();
        await Assert.That(test.DurationLabel).IsEqualTo("");
        test.State = "in-progress";
        await Task.Delay(30);
        test.State = "cancelled";
        double? stopped = test.Capture(ref budget).Duration;
        await Assert.That(stopped > 0).IsTrue();
        await Assert.That(test.RefreshRunningDuration()).IsFalse();
        await Assert.That(test.Capture(ref budget).Duration).IsEqualTo(stopped);
    }

    [Test]
    public async Task StreamsChunksWithoutCopyingTheWholeLogOnEachAppend()
    {
        string chunk = new('x', 100);
        var buffer = new BoundedTextBuffer();
        buffer.Append("warmup"); buffer.Clear();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) buffer.Append(chunk);
        string actual = buffer.GetText();
        long buffered = GC.GetAllocatedBytesForCurrentThread() - start;
        string baseline = "";
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) baseline += chunk;
        long concatenated = GC.GetAllocatedBytesForCurrentThread() - start;
        Console.WriteLine($"200 KB log: chunked {buffered:N0} allocated bytes; repeated concatenation {concatenated:N0} bytes.");
        await Assert.That(actual).IsEqualTo(baseline);
        await Assert.That(buffered < concatenated / 10).IsTrue();
        await Assert.That(ReferenceEquals(actual, buffer.GetText())).IsTrue();
        await Assert.That(buffer.GetText(199_900)).IsEqualTo(chunk);
    }

    [Test]
    public async Task RetainsTheTailAndMarksQueueOverflow()
    {
        var buffer = new BoundedTextBuffer(10_000);
        for (int i = 0; i < 1000; i++) buffer.Append("0123456789");
        buffer.Append("last line");
        await Assert.That(buffer.Length <= 10_000).IsTrue();
        await Assert.That(buffer.Removed > 0).IsTrue();
        await Assert.That(buffer.EndsWith("0123456789last line")).IsTrue();
        await Assert.That(buffer.GetText().EndsWith("last line", StringComparison.Ordinal)).IsTrue();
        long generation = buffer.Generation;
        buffer.Clear(); buffer.Append("new session");
        await Assert.That(buffer.Generation > generation).IsTrue();
        await Assert.That(buffer.GetText()).IsEqualTo("new session");
        var pending = new PendingLogBuffer();
        pending.Append(new string('a', 500_000)); pending.Append("last");
        string text = pending.Drain();
        await Assert.That(text.StartsWith("[Earlier queued output omitted]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(text.EndsWith("last", StringComparison.Ordinal)).IsTrue();
        await Assert.That(pending.IsEmpty).IsTrue();
    }

    [Test]
    public async Task DirtyGroupsPreserveCountsAcrossUpdatesRemovalAndReset()
    {
        var module = new TestModule("project.csproj", "net11.0", "test.dll");
        var root = new TestTreeNode("root", module);
        var group = new TestTreeNode("group", module);
        root.Children.Add(group);
        var passing = new TestTreeNode("passing", module, "1");
        var failing = new TestTreeNode("failing", module, "2");
        group.Children.Add(passing); group.Children.Add(failing);
        passing.State = "passed"; failing.State = "error"; root.RefreshGroup();
        await Assert.That(root.PassedCount).IsEqualTo(1);
        await Assert.That(root.FailedCount).IsEqualTo(1);
        int notifications = 0;
        root.PropertyChanged += (_, _) => notifications++;
        for (int i = 0; i < 1000; i++) root.RefreshGroup();
        await Assert.That(notifications).IsEqualTo(0);
        failing.State = "skipped"; root.RefreshGroup();
        await Assert.That(root.FailedCount).IsEqualTo(0);
        await Assert.That(root.SkippedCount).IsEqualTo(1);
        await Assert.That(root.State).IsEqualTo("passed");
        group.Children.Remove(failing); passing.Reset(); root.RefreshGroup();
        await Assert.That(root.TestCount).IsEqualTo(1);
        await Assert.That(root.PassedCount).IsEqualTo(0);
        passing.Apply(new TestNodeUpdate { StandardOutput = "first" });
        passing.Apply(new TestNodeUpdate { StandardOutput = "first" });
        passing.Apply(new TestNodeUpdate { StandardOutput = "second" });
        await Assert.That(passing.Output).IsEqualTo("first" + Environment.NewLine + "second");
    }
}
