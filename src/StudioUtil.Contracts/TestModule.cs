using System.IO;

namespace StudioUtil.Testing;

internal sealed class TestModule
{
    public TestModule(string projectPath, string framework, string assemblyPath)
    {
        ProjectPath = projectPath;
        Framework = framework;
        AssemblyPath = assemblyPath;
    }

    public string ProjectPath { get; }
    public string Framework { get; }
    public string AssemblyPath { get; }
    public string Key => ProjectPath + "|" + Framework;
    public string DisplayName => Path.GetFileNameWithoutExtension(ProjectPath) + " (" + Framework + ")";
}
