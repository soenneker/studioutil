using System.Threading.Tasks;
using EnvDTE80;

namespace StudioUtil.Utils.Abstract;

public interface IDteUtil
{
    ValueTask<DTE2> GetDte();
}