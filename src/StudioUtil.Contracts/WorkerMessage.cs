using System.Collections.Generic;

namespace StudioUtil.Testing;

// A versioned, data-only boundary: no Visual Studio, Roslyn, or MTP objects cross processes.
internal sealed class WorkerMessage
{
    public int Version { get; set; } = 1;
    public long Id { get; set; }
    public string Kind { get; set; } = "";
    public string? Text { get; set; }
    public string? Path { get; set; }
    public int ProcessId { get; set; }
    public bool Debug { get; set; }
    public TestModule? Module { get; set; }
    public List<TestModule>? Modules { get; set; }
    public List<string>? TestIds { get; set; }
    public List<string>? References { get; set; }
    public List<string>? Values { get; set; }
    public List<TestNodeUpdate>? Updates { get; set; }
    public List<TestDeclaration>? Declarations { get; set; }
    public TestSessionSnapshot? Snapshot { get; set; }
    public FileCommandRequest? FileCommand { get; set; }
}
