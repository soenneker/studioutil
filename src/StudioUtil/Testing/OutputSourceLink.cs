namespace StudioUtil.Testing;

internal sealed class OutputSourceLink
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string Path { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; } = 1;
}
