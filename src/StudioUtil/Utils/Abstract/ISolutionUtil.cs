using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

public interface ISolutionUtil
{
    string GetSolutionFolderPath(EnvDTE.Project folder);

    ValueTask RefreshSolutionExplorer();
}