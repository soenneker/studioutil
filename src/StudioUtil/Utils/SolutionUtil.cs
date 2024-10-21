using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Utils.Abstract;

namespace StudioUtil.Utils;

#nullable enable

///<inheritdoc cref="ISolutionUtil"/>
public class SolutionUtil : ISolutionUtil
{
    private readonly IDteUtil _dteUtil;

    public SolutionUtil(IDteUtil dteUtil)
    {
        _dteUtil = dteUtil;
    }

    public string GetSolutionFolderPath(EnvDTE.Project? folder)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var solutionDirectory = Path.GetDirectoryName(folder!.DTE.Solution.FullName);
        var segments = new List<string>();

        // Record the names of each folder up the 
        // hierarchy until we reach the solution.
        do
        {
            segments.Add(folder.Name);
            folder = folder.ParentProjectItem?.ContainingProject;
        } while (folder != null);

        // Because we walked up the hierarchy, 
        // the path segments are in reverse order.
        segments.Reverse();

        return Path.Combine(new[] { solutionDirectory }.Concat(segments).ToArray());
    }

    public async ValueTask RefreshSolutionExplorer()
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        var dte = await _dteUtil.GetDte();

        dte.Windows.Item(Constants.vsWindowKindSolutionExplorer).Activate();

        dte.Commands.Raise("{1496A755-94DE-11D0-8C3F-00C04FC2AAE2}", 222, null, null);
    }

}