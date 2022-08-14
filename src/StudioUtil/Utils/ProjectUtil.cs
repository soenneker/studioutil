using StudioUtil.Utils.Abstract;
using System.IO;
using System.Threading.Tasks;

namespace StudioUtil.Utils;

#nullable enable

public class ProjectUtil : IProjectUtil
{
    private readonly IDteUtil _dteUtil;

    public ProjectUtil(IDteUtil dteUtil)
    {
        _dteUtil = dteUtil;
    }
    
    public async ValueTask<string?> GetRootFolder(EnvDTE.Project? project)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (project == null)
        {
            return null;
        }

        var dte = await _dteUtil.GetDte();

        if (project.IsKind("{66A26720-8FB5-11D2-AA7E-00C04F688DDE}")) //ProjectKinds.vsProjectKindSolutionFolder
        {
            return Path.GetDirectoryName(dte.Solution.FullName);
        }

        if (string.IsNullOrEmpty(project.FullName))
        {
            return null;
        }

        string? fullPath;

        try
        {
            fullPath = project.Properties.Item("FullPath").Value as string;
        }
        catch (ArgumentException)
        {
            try
            {
                // MFC projects don't have FullPath, and there seems to be no way to query existence
                fullPath = project.Properties.Item("ProjectDirectory").Value as string;
            }
            catch (ArgumentException)
            {
                // Installer projects have a ProjectPath.
                fullPath = project.Properties.Item("ProjectPath").Value as string;
            }
        }

        if (string.IsNullOrEmpty(fullPath))
        {
            return File.Exists(project.FullName) ? Path.GetDirectoryName(project.FullName) : null;
        }

        if (Directory.Exists(fullPath))
        {
            return fullPath;
        }

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath);
        }

        return null;
    }




}