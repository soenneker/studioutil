using Humanizer;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.Directory.Abstract;

namespace StudioUtil.Testing;

internal sealed class FileCommands(IFileUtil fileUtil, IDirectoryUtil directoryUtil)
{
    private readonly IFileUtil _fileUtil = fileUtil;
    private readonly IDirectoryUtil _directoryUtil = directoryUtil;
    private static readonly HttpClient Http = new();

    public async Task<List<string>> CloneAsync(FileCommandRequest request, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrEmpty(request.Target);
        ArgumentException.ThrowIfNullOrEmpty(request.Replacement);
        var replacements = new Dictionary<string, string> { [request.Target] = request.Replacement };
        replacements.TryAdd(request.Target.Camelize(), request.Replacement.Camelize());
        var prepared = new List<(string Path, string Text)>();
        // Read all sources before writing, including when a destination is another source in the batch.
        foreach (string source in request.Sources)
        {
            string directory = request.Directory ?? Path.GetDirectoryName(source)!;
            string name = Path.GetFileName(source).Replace(request.Target, request.Replacement, StringComparison.Ordinal);
            string destination = Destination(directory, name);
            if (string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The replacement must produce a different file path: " + source);
            string content = await _fileUtil.Read(source, log: false, cancellationToken: token);
            foreach (var replacement in replacements) content = content.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            prepared.Add((destination, content));
        }
        return await WriteAsync(prepared, token);
    }

    public async Task<List<string>> GenerateAsync(FileCommandRequest request, CancellationToken token)
    {
        var references = new List<string>();
        foreach (string reference in request.References)
            if (await _fileUtil.Exists(reference, token)) references.Add(reference);
        request.References = references;
        return await WriteAsync(ClipboardCode.Generate(request, token), token);
    }

    public async Task<string> PathKindAsync(string path, CancellationToken token) =>
        await _fileUtil.Exists(path, token) ? "file" : await _directoryUtil.Exists(path, token) ? "directory" : "missing";

    private async Task<List<string>> WriteAsync(List<(string Path, string Text)> prepared, CancellationToken token)
    {
        if (prepared.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != prepared.Count)
            throw new InvalidOperationException("More than one file would be written to the same destination.");
        foreach (var file in prepared) await _fileUtil.WriteAtomically(file.Path, file.Text, log: false, cancellationToken: token);
        return prepared.Select(f => f.Path).ToList();
    }

    public async Task<string> DownloadAsync(FileCommandRequest request, CancellationToken token)
    {
        var uri = new Uri(request.Text!, UriKind.Absolute);
        if (uri.Scheme is not ("http" or "https")) throw new ArgumentException("Enter an HTTP or HTTPS URL.");
        using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var disposition = response.Content.Headers.ContentDisposition;
        string? name = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(name)) name = "downloaded_file_" + DateTime.UtcNow.Ticks;
        string destination = Destination(request.Directory!, name);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        await _fileUtil.WriteAtomically(destination, async (output, cancellation) => await stream.CopyToAsync(output, cancellation), log: false, cancellationToken: token);
        return destination;
    }

    internal static string Destination(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name)
            throw new InvalidOperationException("Invalid destination filename: " + name);
        return Path.GetFullPath(Path.Combine(directory, name));
    }
}
