using System;
using System.Threading.Tasks;

namespace StudioUtil.Testing.Tests;

public sealed class TestOutputSnapshotsTests
{
    [Test]
    public async Task GrowingSnapshotsPreserveRepeatedLinesAndFinalOutput()
    {
        var snapshots = new TestOutputSnapshots();
        var node = new TestTreeNode("test", new TestModule("project", "net11.0", "test.dll"), "1");
        foreach (string output in new[] { "first\n", "first\nnext\n", "first\nnext\nnext\n", "first\nnext\nnext\n" })
            node.Apply(snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = output }));
        node.Apply(snapshots.Apply(new TestNodeUpdate { Uid = "1", ExecutionState = "passed", StandardOutput = "first\nnext\nnext\n" }));
        await Assert.That(node.Output).IsEqualTo("first\nnext\nnext\n");
        await Assert.That(node.State).IsEqualTo("passed");
    }

    [Test]
    public async Task StreamsAndTestsHaveIndependentSnapshots()
    {
        var snapshots = new TestOutputSnapshots();
        snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = "one", StandardError = "warning" });
        snapshots.Apply(new TestNodeUpdate { Uid = "1", ExecutionState = "in-progress" });
        var next = snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = "one two", StandardError = "warning" });
        await Assert.That(next.StandardOutput).IsEqualTo(" two");
        await Assert.That(next.StandardError).IsEqualTo("");
        var other = snapshots.Apply(new TestNodeUpdate { Uid = "2", StandardOutput = "one" });
        await Assert.That(other.StandardOutput).IsEqualTo("one");
        var replaced = snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = "replacement" });
        await Assert.That(replaced.StandardOutput).IsEqualTo("replacement");
        var rerun = new TestOutputSnapshots().Apply(new TestNodeUpdate { Uid = "1", StandardOutput = "replacement" });
        await Assert.That(rerun.StandardOutput).IsEqualTo("replacement");
    }

    [Test]
    public async Task LargeSnapshotsAreComparedBeforeTransportTruncation()
    {
        var snapshots = new TestOutputSnapshots();
        string first = new('x', 90000);
        snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = first, StandardError = "warning" });
        var next = snapshots.Apply(new TestNodeUpdate { Uid = "1", StandardOutput = first + "tail", StandardError = "warning" });
        await Assert.That(next.StandardOutput).IsEqualTo("tail");
        await Assert.That(next.StandardError).IsEqualTo("");
    }
}
