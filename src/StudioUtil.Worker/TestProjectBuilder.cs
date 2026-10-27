using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Utils.File.Abstract;

namespace StudioUtil.Testing;

internal sealed class TestProjectBuilder(IFileUtil fileUtil)
{
    private readonly IFileUtil _fileUtil = fileUtil;
    public async Task<IReadOnlyList<TestModule>> BuildAsync(string project, Action<string> log, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(project)!;
        // Restore before evaluation so imported TUnit/MTP props and centrally managed packages are visible.
        await TestProcess.RunAsync(new[] { "restore", project, "--nologo" }, directory, log, token).ConfigureAwait(false);
        var properties = await EvaluateAsync(project, null, token).ConfigureAwait(false);
        string[] frameworks = Get(properties, "TargetFrameworks").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        if (frameworks.Length == 0)
            frameworks = new[] { Get(properties, "TargetFramework") };

        var modules = new List<TestModule>();
        foreach (string framework in frameworks)
        {
            token.ThrowIfCancellationRequested();
            properties = await EvaluateAsync(project, framework, token).ConfigureAwait(false);
            if (!Get(properties, "IsTestingPlatformApplication").Equals("true", StringComparison.OrdinalIgnoreCase))
                continue;
            log($"Building {Path.GetFileName(project)} ({framework})…");
            var arguments = new List<string> { "build", project, "--configuration", "Debug", "--no-restore", "--nologo" };
            if (!string.IsNullOrEmpty(framework)) arguments.AddRange(new[] { "--framework", framework });
            await TestProcess.RunAsync(arguments, directory, log, token).ConfigureAwait(false);
            properties = await EvaluateAsync(project, framework, token).ConfigureAwait(false);
            string target = Get(properties, "TargetPath");
            if (string.IsNullOrEmpty(target) || !await _fileUtil.Exists(target, token))
                throw new InvalidOperationException($"No runnable test assembly was produced for {project} ({framework}).");
            modules.Add(new TestModule(project, framework, target));
        }
        return modules;
    }

    public static async Task BuildModuleAsync(TestModule module, Action<string> log, CancellationToken token)
    {
        var arguments = new List<string> { "build", module.ProjectPath, "--configuration", "Debug", "--nologo" };
        if (!string.IsNullOrEmpty(module.Framework)) arguments.AddRange(new[] { "--framework", module.Framework });
        await TestProcess.RunAsync(arguments,
            Path.GetDirectoryName(module.ProjectPath)!, log, token).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> EvaluateAsync(string project, string? framework, CancellationToken token)
    {
        var arguments = new List<string> { "msbuild", project, "-nologo", "-p:Configuration=Debug", "-getProperty:IsTestingPlatformApplication,TargetFramework,TargetFrameworks,TargetPath" };
        if (!string.IsNullOrEmpty(framework)) arguments.Add("-p:TargetFramework=" + framework);
        string output = await TestProcess.RunAsync(arguments,
            Path.GetDirectoryName(project)!, null, token).ConfigureAwait(false);
        int start = output.IndexOf('{');
        int end = output.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidOperationException("Unable to evaluate test project. StudioUtil requires .NET SDK 8 or later.\n" + output);
        using var json = JsonDocument.Parse(output.Substring(start, end - start + 1));
        return json.RootElement.GetProperty("Properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
    }

    private static string Get(Dictionary<string, string> properties, string name) => properties.TryGetValue(name, out var value) ? value : "";
}
