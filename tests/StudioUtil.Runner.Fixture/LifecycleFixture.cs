using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TUnit.Core;

namespace StudioUtil.Runner.Fixture;

public sealed class LifecycleFixture
{
    [Test]
    public void LargeOutput() => Console.WriteLine("ARCHIVE_START" + new string('x', 80000) + "ARCHIVE_END");

    [Test]
    public void Failure() => throw new InvalidOperationException("fixture exception for failure actions");

    [Test]
    public async Task ChildProcess()
    {
        using var child = Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        File.WriteAllText(Path.Combine(Path.GetTempPath(), $"studioutil-child-{Environment.ProcessId}.pid"), child.Id.ToString());
        await Task.Delay(TimeSpan.FromMinutes(2));
    }
}
