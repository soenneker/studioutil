using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace StudioUtil.Testing;

/// <summary>Owns a short-lived MTP host, forwards test updates, and closes the host after each operation.</summary>
internal interface ITestSession
{
    /// <summary>Discovers actual runtime test cases, including data-driven cases.</summary>
    Task DiscoverAsync(TestModule module, Action<TestNodeUpdate> update, Action<string> log, CancellationToken token);

    /// <summary>Runs all tests or selected runtime IDs, attaching the IDE debugger before execution when requested.</summary>
    Task RunAsync(TestModule module, IReadOnlyCollection<string>? ids, Func<int, CancellationToken, Task>? attachDebugger,
        Action<TestNodeUpdate> update, Action<string> log, CancellationToken token);
}
