using System.Threading.Tasks;
using StudioUtil.Utils.Abstract;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Nito.AsyncEx;

namespace StudioUtil.Utils;

/// <inheritdoc cref="IDteUtil"/>
public class DteUtil : IDteUtil
{
    private readonly AsyncLazy<DTE2> _dte = new(async () =>
    {
        var service = await ServiceProvider.GetGlobalServiceAsync(typeof(SDTE));
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return service as DTE2 ?? throw new System.InvalidOperationException("Visual Studio automation is unavailable.");
    });
        
    public async ValueTask<DTE2> GetDte()
    {
        var result = await _dte;
        return result;
    }
}
