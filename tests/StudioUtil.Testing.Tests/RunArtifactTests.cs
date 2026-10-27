using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.Directory.Registrars;

namespace StudioUtil.Testing.Tests;

public sealed class RunArtifactTests
{
    [Test]
    public async Task ArchivesBatchSmallWritesAndFlushAtCompletion()
    {
        string root = Path.Combine(Path.GetTempPath(), "StudioUtil-batching-" + Guid.NewGuid().ToString("N"));
        await using var services = new ServiceCollection().AddLogging().AddFileUtilAsSingleton().AddDirectoryUtilAsSingleton().BuildServiceProvider();
        var files = services.GetRequiredService<IFileUtil>();
        var directories = services.GetRequiredService<IDirectoryUtil>();
        try
        {
            using var run = await RunArtifacts.CreateAsync(files, directories, new WorkerMessage { Kind = "run" }, CancellationToken.None, root);
            string log = Path.Combine(run.Folder, "output.log");
            string events = Path.Combine(run.Folder, "events.jsonl");
            for (int i = 0; i < 100; i++) run.Log("batched output");
            run.Update(new TestNodeUpdate { Uid = "test", ExecutionState = "in-progress", StandardOutput = "test output" });
            run.FlushPending();
            await Assert.That(new FileInfo(log).Length).IsEqualTo(0L);
            await Assert.That(new FileInfo(events).Length).IsEqualTo(0L);
            await Task.Delay(2100);
            run.FlushPending();
            await Assert.That(new FileInfo(log).Length > 0).IsTrue();
            await Assert.That(new FileInfo(events).Length > 0).IsTrue();
            run.Log("final output");
            run.Update(new TestNodeUpdate { Uid = "test", ExecutionState = "passed" });
            await run.CompleteAsync(new WorkerMessage { Kind = "complete" });
            long flushedLength = new FileInfo(log).Length;
            run.Dispose();
            await Assert.That(new FileInfo(log).Length).IsEqualTo(flushedLength);
            await Assert.That((await File.ReadAllTextAsync(log)).Contains("test output")).IsTrue();
            await Assert.That((await File.ReadAllTextAsync(log)).Contains("final output")).IsTrue();
            await Assert.That((await File.ReadAllTextAsync(events)).Contains("passed")).IsTrue();
        }
        finally { await directories.Delete(root); }
    }

    [Test]
    public async Task RetentionPreservesActiveRunsAndPartialResults()
    {
        string root = Path.Combine(Path.GetTempPath(), "StudioUtil-artifacts-" + Guid.NewGuid().ToString("N"));
        await using var services = new ServiceCollection().AddLogging().AddFileUtilAsSingleton().AddDirectoryUtilAsSingleton().BuildServiceProvider();
        var files = services.GetRequiredService<IFileUtil>();
        var directories = services.GetRequiredService<IDirectoryUtil>();
        try
        {
            using var active = await RunArtifacts.CreateAsync(files, directories, new WorkerMessage { Kind = "run" }, CancellationToken.None, root);
            string? first = null;
            for (int i = 0; i < 22; i++)
            {
                using var run = await RunArtifacts.CreateAsync(files, directories, new WorkerMessage { Kind = "run" }, CancellationToken.None, root);
                first ??= run.Folder;
                run.Update(new TestNodeUpdate { Uid = "test", ExecutionState = "in-progress", StandardOutput = "partial output survives" });
                await run.CompleteAsync(new WorkerMessage { Kind = "cancelled" });
                string results = await File.ReadAllTextAsync(Path.Combine(run.Folder, "complete.json"));
                await Assert.That(results.Contains("cancelled") && !results.Contains("in-progress")).IsTrue();
                await Task.Delay(2);
            }
            await Assert.That(Directory.Exists(active.Folder)).IsTrue();
            await Assert.That(Directory.Exists(first!)).IsFalse();
            await Assert.That(Directory.GetDirectories(root).Length).IsEqualTo(21);
            active.Update(new TestNodeUpdate { Uid = "stale", ExecutionState = "in-progress" });
            await Assert.That(active.MissingResults(new[] { "stale" }) != null).IsTrue();
        }
        finally { Directory.Delete(root, true); }
    }
}
