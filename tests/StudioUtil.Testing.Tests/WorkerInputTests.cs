using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class WorkerInputTests
{
    [Test]
    public void NativeInputProbe()
    {
        if (OperatingSystem.IsWindows())
            Console.WriteLine("INPUT_TYPE=" + GetFileType(GetStdHandle(-10)));
    }

    [Test]
    public async Task TestHostCanInspectInputWhileWorkerWaitsForCommands()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var client = new WorkerClient(WorkerTests.WorkerPath());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var module = new TestModule("input-probe.csproj", "net11.0", typeof(WorkerInputTests).Assembly.Location);
        string? uid = null;
        await client.DiscoverAsync(module, update =>
        {
            if (update.DisplayName == nameof(NativeInputProbe)) uid = update.Uid;
        }, Console.WriteLine, timeout.Token);
        if (uid == null) throw new InvalidOperationException("Input probe was not discovered.");
        bool passed = false;
        bool isolated = false;
        await client.RunAsync(module, new[] { uid }, null, update =>
        {
            if (update.Uid == uid && update.ExecutionState == "passed") passed = true;
            if (update.StandardOutput?.Contains("INPUT_TYPE=2", StringComparison.Ordinal) == true) isolated = true;
        }, Console.WriteLine, timeout.Token);
        await Assert.That(passed).IsTrue();
        await Assert.That(isolated).IsTrue();
        // The original pipe must still accept requests after the native handle changes.
        await client.GetInfoAsync();
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int kind);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr handle);
}
