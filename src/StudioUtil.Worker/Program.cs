using System.Collections.Concurrent;
using System.Text;
using Soenneker.Utils.Json;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.PooledStringBuilders;

namespace StudioUtil.Testing;

internal static class Program
{
    private static readonly object OutputLock = new();
    private static bool _outputClosed;
    private static readonly ConcurrentDictionary<long, WorkerOperation> Operations = new();

    private static async Task Main()
    {
        WorkerProcessJob.Initialize();
        await using var services = WorkerServices.Create();
        var files = services.GetRequiredService<FileCommands>();
        Console.InputEncoding = new UTF8Encoding(false);
        Console.OutputEncoding = new UTF8Encoding(false);
        var protocolInput = Console.In;
        using var childInput = WorkerStandardInput.IsolateChildProcesses();
        // stdout is exclusively the protocol; test/build output is sent as log events.
        try
        {
            string? line;
            while ((line = await protocolInput.ReadLineAsync()) != null)
            {
                var request = JsonUtil.Deserialize(line, WorkerJsonContext.Default.WorkerMessage) ??
                              throw new InvalidDataException("Empty request.");
                if (request.Version != 1)
                    throw new InvalidDataException("Unsupported StudioUtil protocol version.");
                if (request.Kind is "cancel" or "attached")
                {
                    if (Operations.TryGetValue(request.Id, out var current))
                    {
                        if (request.Kind == "cancel")
                            current.Cancel();
                        else if (request.Text != null)
                            current.Attached.TrySetException(new InvalidOperationException(request.Text));
                        else
                            current.Attached.TrySetResult(true);
                    }

                    continue;
                }

                var operation = new WorkerOperation();
                if (!Operations.TryAdd(request.Id, operation))
                    throw new InvalidDataException("Duplicate request.");
                operation.Completion = Task.Run(() => ExecuteAsync(request, operation, files, services));
            }
        }
        finally
        {
            // Closing VS closes stdin, including after an IDE crash. Always clean up test/build hosts.
            var active = Operations.Values.ToArray();
            foreach (var operation in active)
                operation.Cancel();
            await Task.WhenAll(active.Select(o => o.Completion));
        }
    }

    private static void Send(WorkerMessage message)
    {
        lock (OutputLock)
        {
            if (_outputClosed)
                return;
            try
            {
                Console.WriteLine(JsonUtil.Serialize(message, WorkerJsonContext.Default.WorkerMessage));
            }
            catch (IOException)
            {
                _outputClosed = true;
                foreach (var operation in Operations.Values)
                    operation.Cancel();
            }
        }
    }

    private static async Task ExecuteAsync(WorkerMessage request, WorkerOperation operation, FileCommands files, IServiceProvider services)
    {
        var token = operation.Cancellation.Token;
        var result = new WorkerMessage { Id = request.Id, Kind = "complete" };
        var updates = new List<TestNodeUpdate>();
        var logs = new List<string>();
        int logLength = 0;
        var updateLock = new object();
        RunArtifacts? artifacts = null;
        Exception? flushError = null;
        void Phase(string phase)
        {
            artifacts?.Log($"[{DateTime.UtcNow:O}] Phase: {phase}");
            Send(new WorkerMessage { Id = request.Id, Kind = "phase", Text = phase });
        }

        void Flush()
        {
            lock (updateLock)
            {
                if (updates.Count > 0)
                {
                    Send(new WorkerMessage { Id = request.Id, Kind = "updates", Updates = new(updates) });
                    updates.Clear();
                }
                if (logs.Count > 0)
                {
                    Send(new WorkerMessage { Id = request.Id, Kind = "log", Text = FormatLogs(logs, logLength) });
                    logs.Clear();
                    logLength = 0;
                }
            }
        }

        await using var timer = new Timer(_ =>
        {
            try { Flush(); artifacts?.FlushPending(); }
            catch (Exception ex) { flushError = ex; operation.Cancel(); }
        }, null, 50, 50);

        void Update(TestNodeUpdate update)
        {
            update.ObservedUtc = DateTime.UtcNow;
            artifacts?.Update(update);
            update.StandardOutput = LimitOutput(update.StandardOutput);
            update.StandardError = LimitOutput(update.StandardError);
            update.ErrorMessage = LimitOutput(update.ErrorMessage);
            update.ErrorStackTrace = LimitOutput(update.ErrorStackTrace);
            lock (updateLock)
            {
                updates.Add(update);
                if (updates.Count >= 64)
                    Flush();
            }
        }

        void Log(string text)
        {
            artifacts?.Log(text);
            lock (updateLock)
            {
                string line = LimitOutput(text)!;
                logs.Add(line);
                logLength += line.Length + Environment.NewLine.Length;
                if (logLength >= 32_768) Flush();
            }
        }

        async Task Attach(int pid, CancellationToken cancellation)
        {
            Phase("Attaching debugger");
            Send(new WorkerMessage { Id = request.Id, Kind = "attach", ProcessId = pid });
            await operation.Attached.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        }

        try
        {
            if (request.Kind is "run" or "executeRun")
            {
                artifacts = await RunArtifacts.CreateAsync(services.GetRequiredService<Soenneker.Utils.File.Abstract.IFileUtil>(),
                    services.GetRequiredService<Soenneker.Utils.Directory.Abstract.IDirectoryUtil>(), request, token);
                Send(new WorkerMessage { Id = request.Id, Kind = "artifacts", Path = artifacts.Folder });
            }
            switch (request.Kind)
            {
                case "info":
                    result.ProcessId = Environment.ProcessId;
                    result.Text = Environment.Version.ToString();
                    break;
                case "build":
                    result.Modules = (await services.GetRequiredService<TestProjectBuilder>().BuildAsync(request.Path!, Log, token)).ToList();
                    break;
                case "buildModule":
                    await TestProjectBuilder.BuildModuleAsync(request.Module!, Log, token);
                    break;
                case "discover":
                    await new TestSession().DiscoverAsync(request.Module!, Update, Log, token);
                    break;
                case "executeRun":
                case "run":
                    if (request.Build)
                    {
                        Phase("Building");
                        await TestProjectBuilder.BuildModuleAsync(request.Module!, Log, token);
                    }
                    Phase("Starting host");
                    await new TestSession(Phase).RunAsync(request.Module!, request.TestIds, request.Debug ? Attach : null,
                        Update, Log, token);
                    break;
                case "parse":
                    result.Declarations = TestSourceDiscovery.Discover(request.Path!, request.Text!, token).ToList();
                    break;
                case "load":
                    result.Snapshot = await services.GetRequiredService<TestSessionCache>().LoadAsync(request.Path!, token);
                    break;
                case "save":
                    await services.GetRequiredService<TestSessionCache>().SaveAsync(request.Path!, request.Snapshot!, token);
                    break;
                case "infer":
                    result.Values = CodeInference.Infer(request.Text!, request.References!);
                    break;
                case "cloneFiles":
                    result.Values = await files.CloneAsync(request.FileCommand!, token);
                    break;
                case "generateFiles":
                    result.Values = await files.GenerateAsync(request.FileCommand!, token);
                    break;
                case "downloadFile":
                    result.Path = await files.DownloadAsync(request.FileCommand!, token);
                    break;
                case "documentation":
                    result.Text = DocumentationText.Create(request.Text!);
                    break;
                case "pathKind":
                    result.Text = await files.PathKindAsync(request.Path!, token);
                    break;
                default:
                    throw new InvalidOperationException("Unknown worker operation: " + request.Kind);
            }

            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            result.Kind = "cancelled";
        }
        catch (Exception ex)
        {
            result.Kind = "error";
            result.Text = ex.ToString();
        }
        finally
        {
            await timer.DisposeAsync();
            try
            {
                Flush();
                if (flushError != null) throw new IOException("Could not flush run output.", flushError);
                if (artifacts != null)
                {
                    string? missing = result.Kind == "complete" ? artifacts.MissingResults(request.TestIds) : null;
                    if (missing != null) { result.Kind = "error"; result.Text = missing; }
                    artifacts.Log($"[{DateTime.UtcNow:O}] Finished: {result.Kind}\n{result.Text}");
                    await artifacts.CompleteAsync(result);
                }
            }
            catch (Exception ex) { result.Kind = "error"; result.Text = ex.ToString(); }
            finally
            {
                try { artifacts?.Dispose(); }
                catch (Exception ex) { result.Kind = "error"; result.Text = ex.ToString(); }
            }
            Send(result);
            Operations.TryRemove(request.Id, out _);
            operation.Dispose();
        }
    }

    private static string FormatLogs(List<string> lines, int length)
    {
        using var text = new PooledStringBuilder(length);
        foreach (string line in lines)
        {
            text.Append(line);
            text.Append(Environment.NewLine);
        }
        return text.AsSpan().TrimEnd("\r\n".AsSpan()).ToString();
    }

    private static string? LimitOutput(string? text) => text?.Length > 65_536
        ? "[Earlier output omitted]\n" + text.Substring(text.Length - 65_536) : text;
}
