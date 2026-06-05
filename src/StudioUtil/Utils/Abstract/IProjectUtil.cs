using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

#nullable enable

/// <summary>
/// Defines the project util contract.
/// </summary>
public interface IProjectUtil
{
    /// <summary>
    /// Gets root folder.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <returns>A task containing the result of the operation.</returns>
    ValueTask<string?> GetRootFolder(EnvDTE.Project? project);
}