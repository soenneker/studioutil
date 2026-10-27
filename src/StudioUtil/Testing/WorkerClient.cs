using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing;

internal sealed class WorkerClient : ITestSession, IDisposable
{
    public static WorkerClient Shared { get; } = new();
    private readonly object _gate = new();
    private readonly string _path;
    private WorkerConnection? _connection;
    private bool _disposed;

    public WorkerClient(string? path = null)
    {
        _path = path ?? Path.Combine(Path.GetDirectoryName(typeof(WorkerClient).Assembly.Location)!, "Worker", "StudioUtil.Worker.dll");
    }

    private Task<WorkerMessage> RequestAsync(WorkerMessage message, CancellationToken token,
        Action<TestNodeUpdate>? update = null, Action<string>? log = null, Func<int, CancellationToken, Task>? attach = null, Action<WorkerMessage>? progress = null) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        WorkerConnection connection;
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WorkerClient));
            if (_connection == null || !_connection.IsAlive)
            {
                _connection?.Dispose();
                _connection = new WorkerConnection(_path);
            }
            connection = _connection;
        }
        return await connection.RequestAsync(message, new WorkerPending { Token = token, Update = update, Log = log, Attach = attach, Progress = progress }).ConfigureAwait(false);
    }, token);

    public async Task<IReadOnlyList<TestModule>> BuildAsync(string project, Action<string> log, CancellationToken token) =>
        (await RequestAsync(new WorkerMessage { Kind = "build", Path = project }, token, log: log).ConfigureAwait(false)).Modules!;

    public async Task BuildModuleAsync(TestModule module, Action<string> log, CancellationToken token) =>
        await RequestAsync(new WorkerMessage { Kind = "buildModule", Module = module }, token, log: log).ConfigureAwait(false);

    public async Task DiscoverAsync(TestModule module, Action<TestNodeUpdate> update, Action<string> log, CancellationToken token) =>
        await RequestAsync(new WorkerMessage { Kind = "discover", Module = module }, token, update, log).ConfigureAwait(false);

    public async Task RunAsync(TestModule module, IReadOnlyCollection<string>? ids, Func<int, CancellationToken, Task>? attachDebugger,
        Action<TestNodeUpdate> update, Action<string> log, CancellationToken token) =>
        await RequestAsync(new WorkerMessage { Kind = "run", Module = module, TestIds = ids?.ToList(), Debug = attachDebugger != null },
            token, update, log, attachDebugger).ConfigureAwait(false);

    public async Task<IReadOnlyList<TestDeclaration>> ParseAsync(string file, string text, CancellationToken token) =>
        (await RequestAsync(new WorkerMessage { Kind = "parse", Path = file, Text = text }, token).ConfigureAwait(false)).Declarations!;

    public async Task ExecuteRunAsync(TestModule module, IReadOnlyCollection<string>? ids, bool build,
        Func<int, CancellationToken, Task>? attach, Action<TestNodeUpdate> update, Action<string> log, Action<WorkerMessage> progress, CancellationToken token) =>
        await RequestAsync(new WorkerMessage { Kind = "executeRun", Module = module, TestIds = ids?.ToList(), Build = build, Debug = attach != null },
            token, update, log, attach, progress).ConfigureAwait(false);

    public async Task<TestSessionSnapshot?> LoadAsync(string solution, CancellationToken token) =>
        (await RequestAsync(new WorkerMessage { Kind = "load", Path = solution }, token).ConfigureAwait(false)).Snapshot;

    public async Task SaveAsync(string solution, TestSessionSnapshot snapshot) =>
        await RequestAsync(new WorkerMessage { Kind = "save", Path = solution, Snapshot = snapshot }, CancellationToken.None).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> InferUsingsAsync(string text, List<string> references) =>
        (await RequestAsync(new WorkerMessage { Kind = "infer", Text = text, References = references }, CancellationToken.None).ConfigureAwait(false)).Values!;

    internal Task<WorkerMessage> GetInfoAsync() => RequestAsync(new WorkerMessage { Kind = "info" }, CancellationToken.None);

    public async Task<string> PathKindAsync(string path) =>
        (await RequestAsync(new WorkerMessage { Kind = "pathKind", Path = path }, CancellationToken.None).ConfigureAwait(false)).Text!;

    public async Task<IReadOnlyList<string>> CloneFilesAsync(FileCommandRequest request, CancellationToken token = default) =>
        (await RequestAsync(new WorkerMessage { Kind = "cloneFiles", FileCommand = request }, token).ConfigureAwait(false)).Values!;

    public async Task<IReadOnlyList<string>> GenerateFilesAsync(FileCommandRequest request, CancellationToken token = default) =>
        (await RequestAsync(new WorkerMessage { Kind = "generateFiles", FileCommand = request }, token).ConfigureAwait(false)).Values!;

    public async Task<string> DownloadFileAsync(string uri, string directory, CancellationToken token = default) =>
        (await RequestAsync(new WorkerMessage { Kind = "downloadFile", FileCommand = new FileCommandRequest { Text = uri, Directory = directory } }, token).ConfigureAwait(false)).Path!;

    public async Task<string> DocumentationAsync(string declaration) =>
        (await RequestAsync(new WorkerMessage { Kind = "documentation", Text = declaration }, CancellationToken.None).ConfigureAwait(false)).Text!;

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _connection?.Dispose(); _connection = null; }
    }
}
