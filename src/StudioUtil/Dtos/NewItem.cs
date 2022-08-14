using EnvDTE;

namespace StudioUtil.Dtos;

#nullable enable

public record NewItem
{
    public string? Directory { get; set; }

    public EnvDTE.Project? Project { get; set; }

    public ProjectItem? ProjectItem { get; set; }

    public bool IsSolutionOrSolutionFolder { get; set; }

    public bool IsSolution => IsSolutionOrSolutionFolder && Project == null;

    public bool IsSolutionFolder => IsSolutionOrSolutionFolder && Project != null;
}