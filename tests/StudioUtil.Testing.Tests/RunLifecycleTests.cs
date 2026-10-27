using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class RunLifecycleTests
{
    private static string Project => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(WorkerTests.WorkerPath())!, "../../../../../tests/StudioUtil.Runner.Fixture/StudioUtil.Runner.Fixture.csproj"));

    [Test]
    [NotInParallel("RunLifecycle")]
    public async Task ArchivesFullOutputAndReportsPhasesAndBuildFailures()
    {
        using var client = new WorkerClient(WorkerTests.WorkerPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var module = (await client.BuildAsync(Project, _ => { }, timeout.Token))[0];
        var cases = new Dictionary<string, string>();
        await client.DiscoverAsync(module, u => { if (u.DisplayName != null && u.Uid != null) cases[u.DisplayName] = u.Uid; }, _ => { }, timeout.Token);
        string? folder = null;
        var phases = new List<string>();
        await client.ExecuteRunAsync(module, new[] { cases["LargeOutput"], cases["Failure"] }, true, null, _ => { }, _ => { }, m =>
        { if (m.Kind == "artifacts") folder = m.Path; else phases.Add(m.Text!); }, timeout.Token);
        string output = await File.ReadAllTextAsync(Path.Combine(folder!, "output.log"));
        await Assert.That(output.Contains("ARCHIVE_START") && output.Contains("ARCHIVE_END")).IsTrue();
        await Assert.That(output.Length > 80000).IsTrue();
        string results = await File.ReadAllTextAsync(Path.Combine(folder!, "complete.json"));
        await Assert.That(results.Contains("fixture exception for failure actions")).IsTrue();
        await Assert.That(phases.SequenceEqual(new[] { "Building", "Starting host", "Running" })).IsTrue();
        string? failedFolder = null;
        try
        {
            await client.ExecuteRunAsync(new TestModule(Project + ".missing", module.Framework, module.AssemblyPath), null, true, null, _ => { }, _ => { },
                m => { if (m.Kind == "artifacts") failedFolder = m.Path; }, timeout.Token);
            throw new Exception("Expected build failure");
        }
        catch (InvalidOperationException) { }
        await Assert.That(File.Exists(Path.Combine(failedFolder!, "complete.json"))).IsTrue();
        string? attachFolder = null;
        try
        {
            await client.ExecuteRunAsync(module, new[] { cases["LargeOutput"] }, false, (_, _) => throw new InvalidOperationException("attach rejected"), _ => { }, _ => { },
                m => { if (m.Kind == "artifacts") attachFolder = m.Path; }, timeout.Token);
            throw new Exception("Expected attachment failure");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("attach rejected")) { }
        await Assert.That((await File.ReadAllTextAsync(Path.Combine(attachFolder!, "complete.json"))).Contains("attach rejected")).IsTrue();
        try
        {
            await client.ExecuteRunAsync(module, new[] { "nonexistent-test-id" }, false, null, _ => { }, _ => { }, _ => { }, timeout.Token);
            throw new Exception("Stale discovery silently succeeded");
        }
        catch (InvalidOperationException) { }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("RunLifecycle")]
    public async Task CancellingOrCrashingWorkerTerminatesDescendants(bool crash)
    {
        using var client = new WorkerClient(WorkerTests.WorkerPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var module = (await client.BuildAsync(Project, _ => { }, timeout.Token))[0];
        string? uid = null;
        await client.DiscoverAsync(module, u => { if (u.DisplayName == "ChildProcess") uid = u.Uid; }, _ => { }, timeout.Token);
        int worker = (await client.GetInfoAsync()).ProcessId, host = 0;
        using var cancel = new CancellationTokenSource();
        var run = client.ExecuteRunAsync(module, new[] { uid! }, false, (pid, _) => { host = pid; return Task.CompletedTask; }, _ => { }, _ => { }, _ => { }, cancel.Token);
        string marker = "";
        for (int i = 0; i < 100; i++)
        {
            if (run.IsCompleted) await run;
            marker = Path.Combine(Path.GetTempPath(), $"studioutil-child-{host}.pid");
            if (host != 0 && File.Exists(marker)) break;
            await Task.Delay(100, timeout.Token);
        }
        if (!File.Exists(marker)) { cancel.Cancel(); throw new Exception("Child process did not start"); }
        int child = int.Parse(await File.ReadAllTextAsync(marker));
        try
        {
            if (crash) { using var process = Process.GetProcessById(worker); process.Kill(); }
            else cancel.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(20)); throw new Exception("Run unexpectedly completed"); }
            catch (OperationCanceledException) when (!crash) { }
            catch (IOException) when (crash) { }
            foreach (int pid in new[] { host, child })
            {
                try { using var process = Process.GetProcessById(pid); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (ArgumentException) { }
            }
            // A new request must be usable after either cleanup path.
            await client.GetInfoAsync();
        }
        finally { File.Delete(marker); }
    }
}
