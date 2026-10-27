using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StudioUtil.Dtos;
using StudioUtil.Utils.Abstract;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Project = EnvDTE.Project;
using StudioUtil.Testing;

namespace StudioUtil.Utils;

#nullable enable

/// <inheritdoc cref="INewItemFactory"/>
public class NewItemFactory : INewItemFactory
{
    private readonly ISolutionUtil _solutionUtil;
    private readonly IProjectUtil _projectUtil;

    public NewItemFactory(ISolutionUtil solutionUtil, IProjectUtil projectUtil)
    {
        _solutionUtil = solutionUtil;
        _projectUtil = projectUtil;
    }

    public async ValueTask<NewItem?> Create(DTE2 dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        NewItem? item = null;

        // If a document is active, try to use the document's containing directory.
        if (dte.ActiveWindow is Window2 {Type: vsWindowType.vsWindowTypeDocument})
        {
            item = await CreateFromActiveDocument(dte);
        }

        // If no document was selected, or we could not get a selected item from 
        // the document, then use the selected item in the Solution Explorer window.
        if (item == null)
        {
            item = await CreateFromSolutionExplorerSelection(dte);
        }

        return item;
    }

    private async ValueTask<NewItem?> CreateFromActiveDocument(DTE2 dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var fileName = dte.ActiveDocument?.FullName;

        if (fileName is { Length: > 0 } && await WorkerClient.Shared.PathKindAsync(fileName) == "file")
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var docItem = dte.Solution.FindProjectItem(fileName);
            if (docItem != null)
            {
                return await CreateFromProjectItem(docItem);
            }
        }

        return null;
    }

    private async ValueTask<NewItem?> CreateFromSolutionExplorerSelection(DTE2 dte)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var items = (Array) dte.ToolWindows.SolutionExplorer.SelectedItems;

        if (items.Length == 1)
        {
            var selection = items.Cast<UIHierarchyItem>().First();

            if (selection.Object is EnvDTE.Solution solution)
            {
                var newItem = new NewItem
                {
                    Directory = Path.GetDirectoryName(solution.FullName),
                    IsSolutionOrSolutionFolder = true
                };

                return newItem;
            }

            if (selection.Object is Project project)
            {
                if (project.IsKind(Constants.vsProjectKindSolutionItems))
                {
                    var newItem = new NewItem
                    {
                        Directory = _solutionUtil.GetSolutionFolderPath(project),
                        Project = project,
                        IsSolutionOrSolutionFolder = true
                    };

                    return newItem;
                }
                else
                {
                    var newItem = new NewItem
                    {
                        Directory = await _projectUtil.GetRootFolder(project),
                        Project = project,
                        IsSolutionOrSolutionFolder = false
                    };

                    return newItem;
                }
            }

            if (selection.Object is ProjectItem projectItem)
            {
                return await CreateFromProjectItem(projectItem);
            }
        }

        return null;
    }

    private async ValueTask<NewItem?> CreateFromProjectItem(ProjectItem projectItem)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        if (projectItem.IsKind(Constants.vsProjectItemKindSolutionItems))
        {
            var newItem = new NewItem
            {
                Directory = _solutionUtil.GetSolutionFolderPath(projectItem.ContainingProject),
                Project = projectItem.ContainingProject,
                IsSolutionOrSolutionFolder = true
            };
            return newItem;
        }
        else
        {
            // The selected item needs a directory. This project item could be 
            // a virtual folder, so resolve it to a physical file or folder.
            projectItem = ResolveToPhysicalProjectItem(projectItem);
            var fileName = projectItem.GetFileName();

            if (fileName == null || fileName.Length == 0)
            {
                return null;
            }

            // If the file exists, then it must be a file and we can get the
            // directory name from it. If the file does not exist, then it
            // must be a directory, and the directory name is the file name.
            var directory = await WorkerClient.Shared.PathKindAsync(fileName) == "file" ? Path.GetDirectoryName(fileName) : fileName;
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var newItem = new NewItem
            {
                Directory = directory,
                ProjectItem = projectItem,
                Project = projectItem.ContainingProject,
                IsSolutionOrSolutionFolder = false
            };

            return newItem;
        }
    }

    private static ProjectItem ResolveToPhysicalProjectItem(ProjectItem projectItem)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (projectItem.IsKind(Constants.vsProjectItemKindVirtualFolder))
        {
            // Find the first descendant item that is not a virtual folder.
            return projectItem.ProjectItems
                .Cast<ProjectItem>()
                .Select(item => ResolveToPhysicalProjectItem(item))
                .FirstOrDefault(item => item != null);
        }

        return projectItem;
    }
}
