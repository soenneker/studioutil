using System.Collections.Generic;

namespace StudioUtil.Testing;

internal sealed class FileCommandRequest
{
    public List<string> Sources { get; set; } = new();
    public string? Directory { get; set; }
    public string? Target { get; set; }
    public string? Replacement { get; set; }
    public string? Text { get; set; }
    public string? ProjectDirectory { get; set; }
    public string? RootNamespace { get; set; }
    public List<string> References { get; set; } = new();
}
