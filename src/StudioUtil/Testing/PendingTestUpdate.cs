namespace StudioUtil.Testing;

internal readonly struct PendingTestUpdate
{
    public PendingTestUpdate(TestModule module, TestNodeUpdate update, bool discovery)
    { Module = module; Update = update; Discovery = discovery; }
    public TestModule Module { get; }
    public TestNodeUpdate Update { get; }
    public bool Discovery { get; }
}
