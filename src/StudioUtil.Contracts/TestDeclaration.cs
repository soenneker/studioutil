namespace StudioUtil.Testing;

internal sealed class TestDeclaration
{
    public TestDeclaration(int position, TestSourceTarget target)
    {
        Position = position;
        Target = target;
    }

    public int Position { get; }
    public TestSourceTarget Target { get; }
}
