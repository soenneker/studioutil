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

    public static IWpfTextView GetCurentTextView()
    {
        IComponentModel componentModel = GetComponentModel();

        if (componentModel == null) 
            return null;

        IVsEditorAdaptersFactoryService editorAdapter = componentModel.GetService<IVsEditorAdaptersFactoryService>();
        IVsTextView nativeView = GetCurrentNativeTextView();

        if (nativeView != null)
            return editorAdapter.GetWpfTextView(nativeView);

        return null;
    }

    private static IVsTextView GetCurrentNativeTextView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
        Assumes.Present(textManager);

        textManager.GetActiveView(1, null, out IVsTextView activeView);
        return activeView;
    }

    private static IComponentModel GetComponentModel()
    {
        return (IComponentModel)Package.GetGlobalService(typeof(SComponentModel));
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