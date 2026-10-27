using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil.Testing;

[Guid("a2c739ba-e624-45f7-85bc-f56e3d912c84")]
public sealed class TestRunnerWindow : ToolWindowPane
{
    public TestRunnerWindow() : base(null)
    {
        Caption = "StudioUtil Tests";
        Content = new TestRunnerControl();
    }

    protected override void Dispose(bool disposing)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposing && Content is TestRunnerControl control) control.Dispose();
        base.Dispose(disposing);
    }
}
