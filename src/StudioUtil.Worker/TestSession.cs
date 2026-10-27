using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Testing.Platform.ServerMode.Client;

namespace StudioUtil.Testing;

internal sealed class TestSession : ITestSession
{
    public Task DiscoverAsync(TestModule module, Action<TestNodeUpdate> update, Action<string> log, CancellationToken token) =>
        ExecuteAsync(module, null, true, null, update, log, token);

    public Task RunAsync(TestModule module, IReadOnlyCollection<string>? ids, Func<int, CancellationToken, Task>? attachDebugger,
        Action<TestNodeUpdate> update, Action<string> log, CancellationToken token) =>
        ExecuteAsync(module, ids, false, attachDebugger, update, log, token);

    private static async Task ExecuteAsync(TestModule module, IReadOnlyCollection<string>? ids, bool discover,
        Func<int, CancellationToken, Task>? attachDebugger, Action<TestNodeUpdate> update, Action<string> log, CancellationToken token)
    {
        var options = new MtpServerClientOptions
        {
            ClientName = "StudioUtil", IsStateful = false, ConnectionTimeout = TimeSpan.FromSeconds(30),
            Logger = new DelegateMtpClientLogger((level, message) =>
            {
                if (level >= MtpClientLogLevel.Information) log(message);
            })
        };
        using var client = await MtpServerClient.LaunchAsync(module.AssemblyPath, options, token).ConfigureAwait(false);
        try
        {
            client.TestNodesUpdated += (_, args) =>
            {
                foreach (var change in args.Changes) update(TestUpdateMapper.Map(change));
            };
            client.LogReceived += (_, args) => log($"[{args.Level}] {args.Message}");
            client.AttachmentsReceived += (_, args) =>
            {
                foreach (var attachment in args.Attachments) log($"Attachment: {attachment.DisplayName} {attachment.Uri}");
            };
            var capabilities = await client.InitializeAsync(token).ConfigureAwait(false);
            log($"Connected to {capabilities.ServerName} {capabilities.ServerVersion}: {module.DisplayName}");
            // The host is idle until the run request. Attach now, before any test is dispatched.
            if (attachDebugger != null)
                await attachDebugger(client.ProcessId, token).ConfigureAwait(false);
            if (discover)
            {
                if (!capabilities.SupportsDiscovery)
                    throw new InvalidOperationException("This test host does not support discovery. Update TUnit/Microsoft.Testing.Platform.");
                await client.DiscoverTestsAsync(token).ConfigureAwait(false);
            }
            else
            {
                if (ids != null && ids.Count == 0)
                    throw new InvalidOperationException("No tests were selected.");
                var result = ids == null ? await client.RunTestsAsync(token).ConfigureAwait(false)
                    : await client.RunTestsAsync(ids, token).ConfigureAwait(false);
                foreach (var artifact in result.Artifacts) log($"Artifact: {artifact.DisplayName} {artifact.Uri}");
            }
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.ExitAsync(exitTimeout.Token).ConfigureAwait(false);
        }
        finally
        {
            // Cancellation must also terminate uncooperative test hosts, not just send a protocol cancellation.
            await client.ShutdownAsync().ConfigureAwait(false);
        }
    }
}
