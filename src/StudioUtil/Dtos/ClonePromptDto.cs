namespace StudioUtil.Dtos;

/// <summary>
/// Represents the clone prompt dto.
/// </summary>
public class ClonePromptDto
{
    /// <summary>
    /// Gets or sets a value indicating whether result.
    /// </summary>
    public bool Result { get; set; }

    /// <summary>
    /// Gets or sets target.
    /// </summary>
    public string? Target { get; set; }

    /// <summary>
    /// Gets or sets replacement.
    /// </summary>
    public string? Replacement { get; set; }
}