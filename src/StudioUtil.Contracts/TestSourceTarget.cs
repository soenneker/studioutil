namespace StudioUtil.Testing;

internal sealed class TestSourceTarget
{
    public TestSourceTarget(string file, string name, int firstLine, int lastLine)
    {
        File = file; Name = name; FirstLine = firstLine; LastLine = lastLine;
    }

    public string File { get; }
    public string Name { get; }
    public int FirstLine { get; }
    public int LastLine { get; }

}
