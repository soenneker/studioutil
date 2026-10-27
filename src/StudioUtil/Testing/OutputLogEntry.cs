namespace StudioUtil.Testing;

public sealed class OutputLogEntry
{
    public OutputLogEntry(string text) => Text = text;
    public string Text { get; }
    public override string ToString() => Text;
}
