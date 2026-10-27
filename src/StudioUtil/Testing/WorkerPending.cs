using System;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing;

internal sealed class WorkerPending
{
    public TaskCompletionSource<WorkerMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action<TestNodeUpdate>? Update { get; set; }
    public Action<string>? Log { get; set; }
    public Func<int, CancellationToken, Task>? Attach { get; set; }
    public CancellationToken Token { get; set; }
}
