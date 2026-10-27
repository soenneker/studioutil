using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil.Testing;

internal static class TestSolution
{
    public static IReadOnlyList<string> Projects(DTE dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Project project in dte.Solution.Projects) Add(project, paths);
        return new List<string>(paths);
    }

    private static void Add(Project project, HashSet<string> paths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        // Solution folders have no project file, but may contain nested solution folders and projects.
        if (project.Kind == EnvDTE80.ProjectKinds.vsProjectKindSolutionFolder)
        {
            foreach (ProjectItem item in project.ProjectItems)
                if (item.SubProject != null) Add(item.SubProject, paths);
            return;
        }
        string path = project.FullName;
        if (!string.IsNullOrWhiteSpace(path) && Path.GetExtension(path).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            paths.Add(path);
    }
}
