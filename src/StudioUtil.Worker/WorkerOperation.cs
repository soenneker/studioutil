namespace StudioUtil.Testing;

internal sealed class WorkerOperation : IDisposable
{
    private readonly object _gate = new();
    private bool _disposed;
    public CancellationTokenSource Cancellation { get; } = new();
    public TaskCompletionSource<bool> Attached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion { get; set; } = Task.CompletedTask;
    public void Cancel() { lock (_gate) { if (!_disposed) Cancellation.Cancel(); } }
    public void Dispose() { lock (_gate) { _disposed = true; Cancellation.Dispose(); } }
}
