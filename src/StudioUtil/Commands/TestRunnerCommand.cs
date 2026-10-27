using System.Threading;
using System.Threading.Tasks;
using Community.VisualStudio.Toolkit;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Testing;

namespace StudioUtil.Commands;

[Command(PackageIds.TestRunnerCommand)]
internal sealed class TestRunnerCommand : BaseCommand<TestRunnerCommand>
{
    protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
    {
        var control = await TestRunnerHost.ShowAsync(CancellationToken.None);
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        await control.DiscoverOnOpenAsync();
    }
}
