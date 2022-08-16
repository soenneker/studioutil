using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Utils.Abstract;

namespace StudioUtil.Utils;

#nullable enable

public class SolutionUtil : ISolutionUtil
{
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

}