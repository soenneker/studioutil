using System;
using EnvDTE;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Project = EnvDTE.Project;

namespace StudioUtil.Utils;

#nullable enable

/// <summary>
/// Represents the project helpers.
/// </summary>
public static class ProjectHelpers
{

    /// <summary>
    /// Gets file name.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The result of the operation.</returns>
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

    /// <summary>
    /// Gets curent text view.
    /// </summary>
    /// <returns>The result of the operation.</returns>
    public static IWpfTextView? GetCurentTextView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var componentModel = GetComponentModel();

        if (componentModel == null) 
            return null;

        var editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();
        var nativeView = GetCurrentNativeTextView();

        if (nativeView != null)
            return editorAdapter.GetWpfTextView(nativeView);

        return null;
    }

    private static IVsTextView GetCurrentNativeTextView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
        Assumes.Present(textManager);

        textManager.GetActiveView(1, null, out var activeView);
        return activeView;
    }

    private static IComponentModel GetComponentModel()
    {
        return (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
    }

    /// <summary>
    /// Executes the is kind operation.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="kindGuids">The kind guids.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
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

    /// <summary>
    /// Executes the is kind operation.
    /// </summary>
    /// <param name="projectItem">The project item.</param>
    /// <param name="kindGuids">The kind guids.</param>
    /// <returns>A value indicating whether the operation succeeded.</returns>
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
