using EnvDTE;
using Project = EnvDTE.Project;

namespace StudioUtil.Utils;

#nullable enable

public static class ProjectHelpers
{

    public static string? GetFileName(this ProjectItem item)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            return item.Properties?.Item("FullPath").Value?.ToString();
        }
        catch (ArgumentException)
        {
            // The property does not exist.
            return null;
        }
    }

    public static bool IsKind(this Project project, params string[] kindGuids)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var guid in kindGuids)
        {
            if (project.Kind.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsKind(this ProjectItem projectItem, params string[] kindGuids)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        foreach (var guid in kindGuids)
        {
            if (projectItem.Kind.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}