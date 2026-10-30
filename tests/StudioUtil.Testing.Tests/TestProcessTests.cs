using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class TestProcessTests
{
    [Test]
    public async Task PreservesArgumentsFailureOutputAndCancellationCleanup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "StudioUtil process café " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        int childPid = 0;
        try
        {
            string project = Path.Combine(directory, "test project.proj");
            await File.WriteAllTextAsync(project, """
                <Project>
                  <Target Name="Fail"><Error Text="intentional build failure" /></Target>
                  <Target Name="Wait">
                    <Exec StandardOutputImportance="High" Command="powershell -NoProfile -NonInteractive -Command &quot;Write-Output ('child:' + $PID); Start-Sleep -Seconds 300&quot;" />
                  </Target>
                </Project>
                """);
            try
            {
                await TestProcess.RunAsync(new[] { "msbuild", project, "-t:Fail", "-nologo" }, directory, null, CancellationToken.None);
                throw new Exception("A failing build was reported as successful.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("intentional build failure")) { }

            // Allow cold CI startup while keeping the child alive well beyond the deadline:
            // receiving its PID must cancel the build before the command completes naturally.
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var output = new ConcurrentQueue<string>();
            try
            {
                await TestProcess.RunAsync(new[] { "msbuild", project, "-t:Wait", "-nologo", "-v:normal", "-tl:off" }, directory, line =>
                {
                    output.Enqueue(line);
                    string text = line.Trim();
                    if (!text.StartsWith("child:") || !int.TryParse(text.Substring(6), out int pid)) return;
                    childPid = pid;
                    cancellation.Cancel();
                }, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(75));
                throw new Exception("Cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (childPid == 0)
                throw new Exception("Timed out waiting for the child PID to be streamed within 60 seconds. Build output:\n" +
                    string.Join(Environment.NewLine, output));
            try
            {
                using var child = Process.GetProcessById(childPid);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException) { }
        }
        finally
        {
            if (childPid != 0)
            {
                try { using var child = Process.GetProcessById(childPid); if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (ArgumentException) { }
            }
            Directory.Delete(directory, recursive: true);
        }
    }
}
