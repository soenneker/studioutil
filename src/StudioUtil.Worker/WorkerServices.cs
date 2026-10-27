using Microsoft.Extensions.DependencyInjection;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Directory.Registrars;

namespace StudioUtil.Testing;

internal static class WorkerServices
{
    public static ServiceProvider Create() => new ServiceCollection()
        .AddLogging() // No console provider: stdout belongs to the RPC protocol.
        .AddFileUtilAsSingleton()
        .AddDirectoryUtilAsSingleton()
        .AddSingleton<FileCommands>()
        .AddSingleton<TestProjectBuilder>()
        .AddSingleton<TestSessionCache>()
        .BuildServiceProvider();
}
