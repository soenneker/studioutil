using System;
using System.Collections.Generic;

namespace StudioUtil.Testing;

internal sealed class TestNodeSnapshot
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string Framework { get; set; } = "";
    public string AssemblyPath { get; set; } = "";
    public string? Uid { get; set; }
    public string? FilePath { get; set; }
    public int Line { get; set; }
    public string State { get; set; } = "discovered";
    public string Output { get; set; } = "";
    public string Failure { get; set; } = "";
    public string? RunFolder { get; set; }
    public double? Duration { get; set; }
    public DateTime? LastRunUtc { get; set; }
    public bool IsExpanded { get; set; }
    public List<TestNodeSnapshot> Children { get; set; } = new();
}
