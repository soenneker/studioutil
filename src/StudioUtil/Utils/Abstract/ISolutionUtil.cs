using System.Threading.Tasks;

namespace StudioUtil.Utils.Abstract;

/// <summary>
/// Defines the solution util contract.
/// </summary>
public interface ISolutionUtil
{
    /// <summary>
    /// Gets solution folder path.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The result of the operation.</returns>
    string GetSolutionFolderPath(EnvDTE.Project folder);

    /// <summary>
    /// Executes the refresh solution explorer operation.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    ValueTask RefreshSolutionExplorer();
}