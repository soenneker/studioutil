using EnvDTE;

namespace StudioUtil.Dtos;

/// <summary>
/// Represents the new item record.
/// </summary>
public record NewItem
{
    /// <summary>
    /// Gets or sets directory.
    /// </summary>
    public string? Directory { get; set; }

    /// <summary>
    /// Gets or sets project.
    /// </summary>
    public EnvDTE.Project? Project { get; set; }

    /// <summary>
    /// Gets or sets project item.
    /// </summary>
    public ProjectItem? ProjectItem { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the instance is solution or solution folder.
    /// </summary>
    public bool IsSolutionOrSolutionFolder { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the instance is solution.
    /// </summary>
    public bool IsSolution => IsSolutionOrSolutionFolder && Project == null;

    /// <summary>
    /// Gets or sets a value indicating whether the instance is solution folder.
    /// </summary>
    public bool IsSolutionFolder => IsSolutionOrSolutionFolder && Project != null;
}