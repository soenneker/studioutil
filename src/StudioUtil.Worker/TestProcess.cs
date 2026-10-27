using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Soenneker.Utils.Process;
using Soenneker.Utils.Process.Abstract;
using Soenneker.Utils.Process.Dtos;

namespace StudioUtil.Testing;

internal static class TestProcess
{
    private static readonly IProcessUtil Processes = new ProcessUtil(NullLogger<ProcessUtil>.Instance);

    public static async Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, Action<string>? log, CancellationToken token)
    {
        var output = new StringBuilder();
        var errors = new StringBuilder();
        using var process = await Processes.StartDetached(new ProcessStartDto
        {
            FileName = "dotnet", ArgumentList = arguments, WorkingDirectory = directory,
            RedirectStandardInput = true, Log = false,
            // Null values remove inherited VS overrides; dotnet must resolve the test solution's SDK.
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["MSBUILD_EXE_PATH"] = null!, ["MSBuildSDKsPath"] = null!,
                ["DOTNET_NOLOGO"] = "1", ["NO_COLOR"] = "1",
                // Shared build-server processes can outlive the child whose cancellation we own.
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0", ["MSBUILDDISABLENODEREUSE"] = "1"
            },
            OutputCallback = line => Append(output, line, log),
            ErrorCallback = line => Append(errors, line, log)
        }, token).ConfigureAwait(false) ?? throw new InvalidOperationException("Could not start dotnet.");
        process.StandardInput.Close();
        // ProcessUtil kills the tree on cancellation. Waiting without the cancelled token also drains
        // both callbacks and observes host exit before the caller disposes the process.
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet {string.Join(" ", arguments)} exited with code {process.ExitCode}.\n{errors}\n{output}");
        return output.ToString();
    }

    private static void Append(StringBuilder buffer, string line, Action<string>? log)
    {
        const int limit = 2_000_000;
        if (buffer.Length < limit)
        {
            buffer.Append(line, 0, Math.Min(line.Length, limit - buffer.Length));
            if (buffer.Length < limit) buffer.AppendLine();
        }
        log?.Invoke(line);
    }
}
