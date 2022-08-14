using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

#nullable enable

public interface IProjectUtil
{
    ValueTask<string?> GetRootFolder(EnvDTE.Project? project);
}