using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace StudioUtil.Testing;

internal static class TestRunnerHost
{
    public static async Task<TestRunnerControl> ShowAsync(CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        var service = await AsyncServiceProvider.GlobalProvider.GetServiceAsync(typeof(SVsShell));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        var shell = service as IVsShell ?? throw new InvalidOperationException("Visual Studio's shell is unavailable.");
        Guid guid = PackageGuids.StudioUtil;
        ErrorHandler.ThrowOnFailure(shell.LoadPackage(ref guid, out var package));
        var pane = await ((StudioUtilPackage)package).ShowToolWindowAsync(typeof(TestRunnerWindow), 0, true, token);
        return (TestRunnerControl)(pane?.Content ?? throw new InvalidOperationException("Could not open StudioUtil Tests."));
    }
}
