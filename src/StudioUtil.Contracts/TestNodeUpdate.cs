namespace StudioUtil.Testing;

internal sealed class TestNodeUpdate
{
    public string? Uid { get; set; }
    public string? DisplayName { get; set; }
    public string? NodeType { get; set; }
    public string? ClassName { get; set; }
    public string? FilePath { get; set; }
    public int? LineStart { get; set; }
    public string? ExecutionState { get; set; }
    public double? DurationInMilliseconds { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorStackTrace { get; set; }
    public string? StandardOutput { get; set; }
    public string? StandardError { get; set; }
}
