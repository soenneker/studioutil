using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Testing.Platform.ServerMode.Client;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.Directory.Abstract;

namespace StudioUtil.Testing.Tests;

public sealed class TestSessionCacheTests
{
    private readonly ServiceProvider _services = new ServiceCollection().AddLogging().AddFileUtilAsSingleton().AddDirectoryUtilAsSingleton().BuildServiceProvider();
    private TestSessionCache Cache(string directory) => new(_services.GetRequiredService<IFileUtil>(), _services.GetRequiredService<IDirectoryUtil>(), directory);

    [After(Test)]
    public async Task DisposeServices() => await _services.DisposeAsync();
    [Test]
    public async Task ReadsLegacyCacheAndKeepsItWhenSaveIsCancelled()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudioUtil-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string solution = Path.Combine(directory, "previous.slnx");
            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(solution).ToUpperInvariant())));
            string path = Path.Combine(directory, key + ".json");
            // Written with the original serializer and filename algorithm, independently of the new utilities.
            await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(new TestSessionSnapshot { SessionLog = "previous run ✓" }));
            var cache = Cache(directory);
            Check((await cache.LoadAsync(solution))?.SessionLog == "previous run ✓", "Existing cache was not restored.");
            using var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();
            try
            {
                await cache.SaveAsync(solution, new TestSessionSnapshot { SessionLog = "replacement" }, cancellation.Token);
                throw new InvalidOperationException("Cancelled save succeeded.");
            }
            catch (OperationCanceledException) { }
            Check((await cache.LoadAsync(solution))?.SessionLog == "previous run ✓", "Cancelled write replaced the last good cache.");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "Cancelled write left a temporary cache file.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task RestoresResultsLogsAndSelectionWithoutMixingSolutions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudioUtil-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = Cache(directory);
            var module = new TestModule("tests.csproj", "net10.0", "tests.dll");
            var root = new TestTreeNode(module.DisplayName, module) { IsExpanded = true };
            var group = new TestTreeNode("Example.Tests", module) { IsExpanded = true };
            var test = new TestTreeNode("Passes", module, "case-1");
            test.Apply(TestUpdateMapper.Map(new MtpTestNodeUpdate(new Dictionary<string, object?>
            {
                ["execution-state"] = "passed", ["standardOutput"] = "previous output",
                ["time.duration-ms"] = 12.5, ["location.file"] = "Tests.cs", ["location.line-start"] = 42
            }, null)));
            group.Children.Add(test); root.Children.Add(group);
            int budget = 1024;
            var snapshot = new TestSessionSnapshot { SavedUtc = DateTime.UtcNow, SessionLog = "previous build", SelectedKey = module.Key + "|case-1" };
            snapshot.Roots.Add(root.Capture(ref budget));
            await cache.SaveAsync(Path.Combine(directory, "one.slnx"), snapshot);
            var loaded = (await Cache(directory).LoadAsync(Path.Combine(directory, "one.slnx")))!;
            var restored = TestTreeNode.Restore(loaded.Roots[0]);
            var result = restored.Children[0].Children[0];
            Check(result.State == "passed" && result.Output == "previous output" && result.DurationLabel == "12.5 ms", "Result details did not survive reopening.");
            Check(result.LastRunUtc == test.LastRunUtc && result.Line == 42 && result.FilePath == "Tests.cs", "Source or run timestamp was lost.");
            Check(restored.IsExpanded && restored.Children[0].IsExpanded && restored.TestCount == 1, "Tree structure was lost.");
            Check(loaded.SelectedKey == snapshot.SelectedKey && loaded.SessionLog == "previous build", "Session context was lost.");
            Check(await cache.LoadAsync(Path.Combine(directory, "two.slnx")) == null, "Different solutions shared results.");
            snapshot.SessionLog = "replacement";
            await cache.SaveAsync(Path.Combine(directory, "one.slnx"), snapshot);
            Check((await cache.LoadAsync(Path.Combine(directory, "one.slnx")))!.SessionLog == "replacement", "Existing cache could not be replaced.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Test]
    public void RediscoveryPreservesPreviousResultAndUpdatesSourceLocation()
    {
        var test = new TestTreeNode("Fails", new TestModule("test.csproj", "net10.0", "test.dll"), "case-1");
        test.Apply(TestUpdateMapper.Map(new MtpTestNodeUpdate(new Dictionary<string, object?>
        {
            ["execution-state"] = "failed", ["standardError"] = "failure log", ["time.duration-ms"] = 23.0
        }, null)));
        DateTime? lastRun = test.LastRunUtc;
        test.Apply(TestUpdateMapper.Map(new MtpTestNodeUpdate(new Dictionary<string, object?>
        {
            ["execution-state"] = "discovered", ["location.line-start"] = 99, ["location.file"] = "Moved.cs"
        }, null)), discovery: true);
        Check(test.State == "failed" && test.Output == "failure log" && test.DurationLabel == "23 ms" && test.LastRunUtc == lastRun,
            "Discovery overwrote the previous run.");
        Check(test.Line == 99 && test.FilePath == "Moved.cs", "Discovery did not refresh source metadata.");
        test.Reset();
        int budget = 1024;
        Check(TestTreeNode.Restore(test.Capture(ref budget)).State == "cancelled", "Reopened cache reports an interrupted test as still running.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
