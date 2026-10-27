using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class FileCommandTests
{
    [Test]
    public async Task ClonesAndGeneratesFilesAcrossTheProcessBoundary()
    {
        using var worker = new WorkerClient(WorkerTests.WorkerPath());
        string directory = Path.Combine(Path.GetTempPath(), "StudioUtil commands " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string source = Path.Combine(directory, "Customer.cs");
            const string original = "class Customer { string customer = \"✓\"; }\r\n";
            await File.WriteAllTextAsync(source, original);
            var cloned = await worker.CloneFilesAsync(new FileCommandRequest { Sources = new() { source }, Target = "Customer", Replacement = "Supplier" });
            Check(cloned.Single() == Path.Combine(directory, "Supplier.cs"), "Clone filename was not replaced.");
            Check(await File.ReadAllTextAsync(cloned[0]) == "class Supplier { string supplier = \"✓\"; }\r\n", "Case replacement, Unicode, or line endings changed.");
            Check(await File.ReadAllTextAsync(source) == original, "Clone changed its source.");
            try
            {
                await worker.CloneFilesAsync(new FileCommandRequest { Sources = new() { source }, Target = "Absent", Replacement = "Other" });
                throw new Exception("Clone allowed overwriting its source.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("different file path")) { }

            string nested = Path.Combine(directory, "feature-area");
            Directory.CreateDirectory(nested);
            var generated = await worker.GenerateFilesAsync(new FileCommandRequest
            {
                Directory = nested, ProjectDirectory = directory, RootNamespace = "Example", References = new() { typeof(object).Assembly.Location },
                Text = """
                    using System;
                    // keep this comment
                    public class First { public string Braces => "{ }"; public class Nested {} }
                    public record Second(int Value);
                    public delegate void Notify(string value);
                    """
            });
            Check(generated.Count == 3 && !File.Exists(Path.Combine(nested, "Nested.cs")), "Top-level splitting produced the wrong files.");
            string first = await File.ReadAllTextAsync(Path.Combine(nested, "First.cs"));
            Check(first.Contains("namespace Example.feature_area;") && first.Contains("using System;") && first.Contains("keep this comment") && first.Contains("class Nested"), "Namespace, usings, comments, or nested types were lost.");
            var explicitNamespace = await worker.GenerateFilesAsync(new FileCommandRequest
            {
                Directory = nested, Text = "namespace Original { using System; public class Third {} public class Fourth {} }"
            });
            Check(explicitNamespace.Count == 2 && (await File.ReadAllTextAsync(explicitNamespace[0])).Contains("namespace Original"), "An explicit namespace was replaced.");
            Check(await worker.DocumentationAsync("public class Example : IExample") == "///<inheritdoc cref=\"IExample\"/>", "Documentation preparation changed.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [Test]
    public async Task DownloadsHeaderFilenameAndRejectsDirectoryTraversal()
    {
        using var worker = new WorkerClient(WorkerTests.WorkerPath());
        string directory = Path.Combine(Path.GetTempPath(), "StudioUtil download " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/resource";
        try
        {
            Task response = Respond(listener, "download.cs", timeout.Token);
            string file = await worker.DownloadFileAsync(url, directory, timeout.Token);
            await response;
            Check(file == Path.Combine(directory, "download.cs") && await File.ReadAllTextAsync(file) == "download ✓", "Download filename or content was lost.");
            response = Respond(listener, "../outside.cs", timeout.Token);
            try
            {
                await worker.DownloadFileAsync(url, directory, timeout.Token);
                throw new Exception("Download accepted a filename outside the selected directory.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Invalid destination filename")) { }
            await response;
        }
        finally { listener.Stop(); Directory.Delete(directory, true); }
    }

    private static async Task Respond(TcpListener listener, string name, CancellationToken token)
    {
        using var client = await listener.AcceptTcpClientAsync(token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(token))) { }
        byte[] body = Encoding.UTF8.GetBytes("download ✓");
        byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Disposition: attachment; filename=\"{name}\"\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(body, token);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
