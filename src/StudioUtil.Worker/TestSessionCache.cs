using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Hashing.Sha256;
using Soenneker.Hashing.Sha256.Abstract;
using Soenneker.Utils.Json;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;

namespace StudioUtil.Testing;

internal sealed class TestSessionCache
{
    private readonly string _directory;
    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private static readonly ISha256HashingUtil Hashing = new Sha256HashingUtil();

    public TestSessionCache(IFileUtil fileUtil, IDirectoryUtil directoryUtil, string? directory = null)
    {
        _fileUtil = fileUtil;
        _directoryUtil = directoryUtil;
        _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StudioUtil", "TestSessions");
    }

    public async ValueTask<TestSessionSnapshot?> LoadAsync(string solution, CancellationToken token = default)
    {
        string path = GetPath(solution);
        long? size = await _fileUtil.GetSize(path, token);
        if (size == null || size > 32 * 1024 * 1024) return null;
        using var stream = _fileUtil.OpenRead(path, log: false);
        var snapshot = await JsonUtil.Deserialize(stream, WorkerJsonContext.Default.TestSessionSnapshot, cancellationToken: token).ConfigureAwait(false);
        return snapshot?.Version == 1 ? snapshot : null;
    }

    public async Task SaveAsync(string solution, TestSessionSnapshot snapshot, CancellationToken token = default)
    {
        await _directoryUtil.Create(_directory, log: false, cancellationToken: token);
        string path = GetPath(solution);
        await _fileUtil.WriteAtomically(path, async (stream, cancellation) =>
        {
            await JsonUtil.SerializeToStream(stream, snapshot, WorkerJsonContext.Default.TestSessionSnapshot, cancellation).ConfigureAwait(false);
            if (stream.Length > 32 * 1024 * 1024)
                throw new IOException("Test session cache exceeded its size limit.");
        }, log: false, cancellationToken: token);
    }

    private string GetPath(string solution)
    {
        // Preserve existing uppercase filenames, so installed users retain their previous results.
        string key = Hashing.Hash(Path.GetFullPath(solution).ToUpperInvariant()).ToUpperInvariant();
        return Path.Combine(_directory, key + ".json");
    }
}
