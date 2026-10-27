using System;
using System.Collections.Generic;

namespace StudioUtil.Testing;

internal sealed class TestSessionSnapshot
{
    public int Version { get; set; } = 1;
    public DateTime SavedUtc { get; set; }
    public string SessionLog { get; set; } = "";
    public string? SelectedKey { get; set; }
    public List<TestNodeSnapshot> Roots { get; set; } = new();
}
