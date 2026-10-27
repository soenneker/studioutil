using System;
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
                    <Exec Command="powershell -NoProfile -Command &quot;Write-Output ('child:' + $PID); Start-Sleep -Seconds 60&quot;" />
                  </Target>
                </Project>
                """);
            try
            {
                await TestProcess.RunAsync(new[] { "msbuild", project, "-t:Fail", "-nologo" }, directory, null, CancellationToken.None);
                throw new Exception("A failing build was reported as successful.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("intentional build failure")) { }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await TestProcess.RunAsync(new[] { "msbuild", project, "-t:Wait", "-nologo" }, directory, line =>
                {
                    string text = line.Trim();
                    if (!text.StartsWith("child:") || !int.TryParse(text.Substring(6), out int pid)) return;
                    childPid = pid;
                    cancellation.Cancel();
                }, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(25));
                throw new Exception("Cancellation was ignored.");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            if (childPid == 0) throw new Exception("Build output was not streamed before completion.");
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
