using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;

namespace StudioUtil.Testing;

internal sealed class RunArtifacts : IDisposable
{
    private readonly StreamWriter _events;
    private readonly StreamWriter _log;
    private readonly object _gate = new();
    private readonly Dictionary<string, TestNodeUpdate> _results = new();
    private readonly IFileUtil _fileUtil;
    private readonly DateTime _started = DateTime.UtcNow;
    public string Folder { get; }

    private RunArtifacts(string folder, IFileUtil fileUtil)
    {
        Folder = folder; _fileUtil = fileUtil;
        _events = new StreamWriter(fileUtil.OpenWrite(Path.Combine(folder, "events.jsonl"), false), new UTF8Encoding(false), 65536);
        try { _log = new StreamWriter(fileUtil.OpenWrite(Path.Combine(folder, "output.log"), false), new UTF8Encoding(false), 65536); }
        catch { _events.Dispose(); throw; }
    }

    public static async Task<RunArtifacts> CreateAsync(IFileUtil fileUtil, IDirectoryUtil directoryUtil, WorkerMessage request, CancellationToken token, string? directory = null)
    {
        string root = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioUtil", "Runs"));
        await directoryUtil.Create(root, false, token);
        // Keep the newest 20 runs, including this one. Never delete an active run.
        int completed = 0;
        foreach (string candidate in (await directoryUtil.GetAllDirectories(root, token)).OrderByDescending(Path.GetFileName))
        {
            string resolved = Path.GetFullPath(candidate);
            if (!string.Equals(Path.GetDirectoryName(resolved), root, StringComparison.OrdinalIgnoreCase)) continue;
            string name = Path.GetFileName(resolved);
            if (name.Length != 52 || !Guid.TryParseExact(name.Substring(20), "N", out _)) continue;
            if (!await fileUtil.Exists(Path.Combine(resolved, "run.json"), cancellationToken: token)) continue;
            if (!await fileUtil.Exists(Path.Combine(resolved, "complete.json"), cancellationToken: token))
            {
                try
                {
                    using var metadata = fileUtil.OpenRead(Path.Combine(resolved, "run.json"), false);
                    var owner = await JsonSerializer.DeserializeAsync(metadata, WorkerJsonContext.Default.WorkerMessage, token);
                    if (owner == null || IsOwnerAlive(owner.ProcessId, name)) continue;
                }
                catch (JsonException) { continue; }
                catch (IOException) { continue; }
            }
            if (++completed >= 20)
            {
                try { await directoryUtil.Delete(resolved, token); }
                catch (IOException) { } // Another worker may still be closing this completed run.
                catch (UnauthorizedAccessException) { }
            }
        }
        string folder = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N"));
        await directoryUtil.Create(folder, false, token);
        request.ProcessId = Environment.ProcessId;
        await fileUtil.WriteAtomically(Path.Combine(folder, "run.json"), async (stream, ct) =>
            await JsonSerializer.SerializeAsync(stream, request, WorkerJsonContext.Default.WorkerMessage, ct), log: false, cancellationToken: token);
        return new RunArtifacts(folder, fileUtil);
    }

    private static bool IsOwnerAlive(int pid, string name)
    {
        if (pid <= 0) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return DateTime.TryParseExact(name.Substring(0, 19), "yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var started)
                && process.StartTime.ToUniversalTime() <= started;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }

    public void Log(string text)
    {
        lock (_gate) _log.WriteLine(text);
    }

    public void Update(TestNodeUpdate update)
    {
        lock (_gate)
        {
            _events.WriteLine(JsonSerializer.Serialize(update, WorkerJsonContext.Default.TestNodeUpdate));
            if (update.Uid != null && update.NodeType != "group")
            {
                _results.TryGetValue(update.Uid, out var old);
                _results[update.Uid] = new TestNodeUpdate { Uid = update.Uid, DisplayName = update.DisplayName ?? old?.DisplayName,
                    ExecutionState = update.ExecutionState ?? old?.ExecutionState, DurationInMilliseconds = update.DurationInMilliseconds ?? old?.DurationInMilliseconds,
                    FilePath = update.FilePath ?? old?.FilePath, LineStart = update.LineStart ?? old?.LineStart,
                    ErrorMessage = update.ErrorMessage ?? old?.ErrorMessage, ErrorStackTrace = update.ErrorStackTrace ?? old?.ErrorStackTrace };
            }
            foreach (var text in new[] { update.StandardOutput, update.StandardError, update.ErrorMessage, update.ErrorStackTrace })
                if (!string.IsNullOrEmpty(text)) _log.WriteLine($"[{update.Uid}] {text}");
        }
    }

    public void Flush() { lock (_gate) { _events.Flush(); _log.Flush(); } }

    public string? MissingResults(IReadOnlyCollection<string>? ids)
    {
        var missing = (ids ?? _results.Keys).Where(id => !_results.TryGetValue(id, out var result) ||
            result.ExecutionState is not ("passed" or "failed" or "error" or "timeout" or "skipped" or "cancelled")).Take(10).ToArray();
        return missing.Length > 0 || _results.Count == 0
            ? "The test host returned without final results for selected tests. Refresh discovery and retry. " + string.Join(", ", missing) : null;
    }

    public async Task CompleteAsync(WorkerMessage result)
    {
        Flush();
        foreach (var test in _results.Values)
            if (test.ExecutionState is null or "queued" or "in-progress" or "discovered")
                test.ExecutionState = result.Kind == "cancelled" ? "cancelled" : "error";
        var summary = new WorkerMessage { Kind = result.Kind, Text = result.Text, Path = Folder, Updates = _results.Values.ToList(),
            Values = new() { "StartedUtc=" + _started.ToString("O"), "FinishedUtc=" + DateTime.UtcNow.ToString("O") } };
        await _fileUtil.WriteAtomically(Path.Combine(Folder, "complete.json"), async (stream, ct) =>
            await JsonSerializer.SerializeAsync(stream, summary, WorkerJsonContext.Default.WorkerMessage, ct), log: false);
    }

    public void Dispose() { _events.Dispose(); _log.Dispose(); }
}
