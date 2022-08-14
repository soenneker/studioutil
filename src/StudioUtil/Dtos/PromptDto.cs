namespace StudioUtil.Dtos;

#nullable enable

public class PromptDto
{
    public bool Result { get; set; }

    public string? Target { get; set; }

    public string? Replacement { get; set; }
}