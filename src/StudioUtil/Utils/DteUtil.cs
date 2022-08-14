using System.Threading.Tasks;
using StudioUtil.Utils.Abstract;
using EnvDTE80;
using Microsoft.VisualStudio.Shell.Interop;
using Nito.AsyncEx;

namespace StudioUtil.Utils;

public class DteUtil : IDteUtil
{
    private readonly AsyncLazy<DTE2> _dte = new(async () =>
    {
        var result = await ServiceProvider.GetGlobalServiceAsync(typeof(SDTE)) as DTE2;
        return result!;
    });
        
    public async ValueTask<DTE2> GetDte()
    {
        var result = await _dte;
        return result;
    }
}