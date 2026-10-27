using System;
using System.Collections.Generic;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using StudioUtil.Dtos;

namespace StudioUtil.Utils;

internal static class CommandFiles
{
    public static void Add(DTE2 dte, NewItem target, string solution, IEnumerable<string> paths)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!dte.Solution.IsOpen || !string.Equals(dte.Solution.FullName, solution, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The files were saved, but the solution changed before they could be added.");
        var project = target.Project;
        if (project == null)
        {
            foreach (Project candidate in dte.Solution.Projects)
                if (candidate.Name == "Solution Items") { project = candidate; break; }
            project ??= ((Solution2)dte.Solution).AddSolutionFolder("Solution Items");
        }
        foreach (string path in paths) project.ProjectItems.AddFromFile(path);
    }
}
