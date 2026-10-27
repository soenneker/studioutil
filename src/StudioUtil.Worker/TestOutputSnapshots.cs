using System;
using System.Collections.Generic;

namespace StudioUtil.Testing;

internal sealed class TestOutputSnapshots
{
    private readonly Dictionary<string, string> _stdout = new();
    private readonly Dictionary<string, string> _stderr = new();

    public TestNodeUpdate Apply(TestNodeUpdate update)
    {
        if (update.Uid == null) return update;
        update.StandardOutput = Delta(_stdout, update.Uid, update.StandardOutput);
        update.StandardError = Delta(_stderr, update.Uid, update.StandardError);
        update.OutputIsDelta = true;
        return update;
    }

    private static string? Delta(Dictionary<string, string> snapshots, string uid, string? value)
    {
        // Missing output properties do not clear the last snapshot.
        if (string.IsNullOrEmpty(value)) return null;
        snapshots.TryGetValue(uid, out var previous);
        snapshots[uid] = value;
        return previous != null && value.StartsWith(previous, StringComparison.Ordinal)
            ? value.Substring(previous.Length) : value;
    }
}
