using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class WorkerTests
{
    internal static string WorkerPath()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "StudioUtil.slnx"))) directory = Directory.GetParent(directory)!.FullName;
        return Path.Combine(directory, "src", "StudioUtil.Worker", "bin",
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "net11.0", "StudioUtil.Worker.dll");
    }

    [Test]
    public async Task WorkerUsesNet11RestoresCacheAndRecoversAfterExit()
    {
        using var client = new WorkerClient(WorkerPath());
        var info = await client.GetInfoAsync();
        Check(info.ProcessId != Environment.ProcessId && info.Text!.StartsWith("11."), "Work is not running in a .NET 11 child process.");
        string solution = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".slnx");
        string cachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioUtil", "TestSessions",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(solution).ToUpperInvariant()))) + ".json");
        try
        {
            await client.SaveAsync(solution, new TestSessionSnapshot { SessionLog = "Unicode ✓ café 日本語\nsecond line", SelectedKey = "case" });
            using (var process = Process.GetProcessById(info.ProcessId))
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            var restored = await client.LoadAsync(solution, CancellationToken.None);
            Check(restored?.SessionLog == "Unicode ✓ café 日本語\nsecond line" && restored.SelectedKey == "case", "Cache did not survive worker restart.");
            var next = await client.GetInfoAsync();
            Check(next.ProcessId != info.ProcessId, "Disconnected worker was not restarted.");
            var requests = Enumerable.Range(0, 20).Select(i => client.ParseAsync("é.cs", $"class C {{ [Test] void Test{i}() {{}} }}", CancellationToken.None)).ToArray();
            var declarations = await Task.WhenAll(requests);
            Check(declarations.Select((d, i) => d.Count == 1 && d[0].Target.Name == "Test" + i && d[0].Target.File == "é.cs").All(x => x), "Concurrent responses were mixed or lost.");
            var usings = await client.InferUsingsAsync("class C { [JsonPropertyName(\"name\")] public string Name { get; set; } }", new() { typeof(object).Assembly.Location });
            Check(usings.Contains("using System.Text.Json.Serialization;"), "Clipboard inference was lost across the worker boundary.");
        }
        finally { if (File.Exists(cachePath)) File.Delete(cachePath); }
    }

    [Test]
    [NotInParallel("StudioUtilFixture")]
    public async Task ClosingConnectionCancelsActiveHost()
    {
        using var client = new WorkerClient(WorkerPath());
        // The integration test builds this fixture; independently build it if this test runs alone.
        string project = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(WorkerPath())!, "../../../../../tests/StudioUtil.Testing.Fixture/StudioUtil.Testing.Fixture.csproj"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var modules = await client.BuildAsync(project, _ => { }, timeout.Token);
        string? slow = null;
        await client.DiscoverAsync(modules[0], u => { if (u.DisplayName?.Contains("Slow") == true) slow = u.Uid; }, _ => { }, timeout.Token);
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        int pid = 0;
        var run = client.RunAsync(modules[0], new[] { slow! }, (id, _) => { pid = id; return Task.CompletedTask; },
            u => { if (u.ExecutionState == "in-progress") started.TrySetResult(pid); }, _ => { }, timeout.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        client.Dispose();
        try { await run; throw new InvalidOperationException("Disposed connection completed an active run successfully."); }
        catch (ObjectDisposedException) { }
        try
        {
            using var host = Process.GetProcessById(pid);
            await host.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (ArgumentException) { }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
