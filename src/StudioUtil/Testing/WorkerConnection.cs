using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing;

internal sealed class WorkerConnection : IDisposable
{
    private readonly Process _process;
    private readonly object _writeGate = new();
    private readonly ConcurrentDictionary<long, WorkerPending> _pending = new();
    private long _nextId;
    private bool _closed;

    public WorkerConnection(string path)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo("dotnet", "\"" + path + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(path)!
            }
        };
        _process.StartInfo.EnvironmentVariables.Remove("MSBUILD_EXE_PATH");
        _process.StartInfo.EnvironmentVariables.Remove("MSBuildSDKsPath");
        _process.StartInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
        _process.Start();
        _ = ReadAsync();
    }

    public bool IsAlive { get { lock (_writeGate) return !_closed && !_process.HasExited; } }

    public async Task<WorkerMessage> RequestAsync(WorkerMessage message, WorkerPending pending)
    {
        message.Id = Interlocked.Increment(ref _nextId);
        lock (_writeGate)
        {
            if (_closed) throw new IOException("StudioUtil worker disconnected. Retry the operation to restart it.");
            _pending[message.Id] = pending;
            try { Write(message); }
            catch { _pending.TryRemove(message.Id, out _); throw; }
        }
        using var registration = pending.Token.Register(() => { _ = CancelAsync(message.Id, pending); });
        // Completed by the pipe reader, not UI-thread work; this is an external RPC signal.
#pragma warning disable VSTHRD003
        return await pending.Completion.Task.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    private void Write(WorkerMessage message)
    {
        lock (_writeGate)
        {
            if (_closed) throw new IOException("StudioUtil worker disconnected.");
            _process.StandardInput.WriteLine(JsonSerializer.Serialize(message));
            _process.StandardInput.Flush();
        }
    }

    private async Task CancelAsync(long id, WorkerPending pending)
    {
        try
        {
            Write(new WorkerMessage { Id = id, Kind = "cancel" });
            // The completion source is an external RPC signal; it has no joinable task to join.
#pragma warning disable VSTHRD003
            if (await Task.WhenAny(pending.Completion.Task, Task.Delay(10000)).ConfigureAwait(false) != pending.Completion.Task)
#pragma warning restore VSTHRD003
                Dispose();
        }
        catch (Exception ex) { pending.Completion.TrySetException(ex); }
    }

    private async Task ReadAsync()
    {
        Exception failure = new IOException("StudioUtil worker stopped unexpectedly.");
        try
        {
            var stderr = _process.StandardError.ReadToEndAsync();
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                var message = JsonSerializer.Deserialize<WorkerMessage>(line) ?? throw new InvalidDataException("Invalid worker response.");
                if (message.Version != 1) throw new InvalidDataException("StudioUtil worker protocol mismatch. Redeploy the extension.");
                if (!_pending.TryGetValue(message.Id, out var pending)) continue;
                switch (message.Kind)
                {
                    case "updates": foreach (var update in message.Updates!) pending.Update?.Invoke(update); break;
                    case "log": pending.Log?.Invoke(message.Text!); break;
                    case "attach": _ = AttachAsync(message, pending); break;
                    case "complete": _pending.TryRemove(message.Id, out _); pending.Completion.TrySetResult(message); break;
                    case "cancelled": _pending.TryRemove(message.Id, out _); pending.Completion.TrySetCanceled(); break;
                    case "error": _pending.TryRemove(message.Id, out _); pending.Completion.TrySetException(new InvalidOperationException(message.Text)); break;
                    default: throw new InvalidDataException("Unknown worker response: " + message.Kind);
                }
            }
            string diagnostics = await stderr.ConfigureAwait(false);
            failure = new IOException("StudioUtil's .NET 11 worker exited. Ensure the .NET 11 runtime is installed.\n" + diagnostics);
        }
        catch (Exception ex) { failure = ex; }
        finally { Close(failure); }
    }

    private async Task AttachAsync(WorkerMessage message, WorkerPending pending)
    {
        string? error = null;
        try
        {
            if (pending.Attach == null) throw new InvalidOperationException("No debugger attachment callback is available.");
            await pending.Attach(message.ProcessId, pending.Token).ConfigureAwait(false);
        }
        catch (Exception ex) { error = ex.ToString(); }
        try { Write(new WorkerMessage { Id = message.Id, Kind = "attached", Text = error }); }
        catch (Exception ex) { pending.Completion.TrySetException(ex); }
    }

    private void Close(Exception reason)
    {
        lock (_writeGate)
        {
            if (_closed) return;
            _closed = true;
            foreach (var pair in _pending)
                if (_pending.TryRemove(pair.Key, out var pending))
                {
                    if (pending.Token.IsCancellationRequested) pending.Completion.TrySetCanceled();
                    else pending.Completion.TrySetException(reason);
                }
            try { _process.StandardInput.Close(); } catch (IOException) { }
        }
        _ = Task.Run(() =>
        {
            try
            {
                if (!_process.WaitForExit(5000))
                {
                    using var kill = Process.Start(new ProcessStartInfo("taskkill.exe", $"/PID {_process.Id} /T /F")
                    { UseShellExecute = false, CreateNoWindow = true });
                    kill?.WaitForExit(5000);
                }
            }
            finally { _process.Dispose(); }
        });
    }

    public void Dispose() => Close(new ObjectDisposedException(nameof(WorkerConnection)));
}
