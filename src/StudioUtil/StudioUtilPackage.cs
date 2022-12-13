using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using StudioUtil.Commands;
using StudioUtil.Utils;
using StudioUtil.Utils.Abstract;
using Community.VisualStudio.Toolkit.DependencyInjection.Microsoft;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;

namespace StudioUtil;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration(Vsix.Name, Vsix.Description, Vsix.Version)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuids.StudioUtilString)]
public sealed class StudioUtilPackage : MicrosoftDIToolkitPackage<StudioUtilPackage>
{
    protected override void InitializeServices(IServiceCollection services)
    {
        // Register your services here
        services.AddSingleton<IFileUtilSync, FileUtilSync>();
        services.AddSingleton<INewItemFactory, NewItemFactory>();
        services.AddSingleton<IProjectUtil, ProjectUtil>();
        services.AddSingleton<IVariablesUtil, VariablesUtil>();
        services.AddSingleton<ISolutionUtil, SolutionUtil>();

        // Register any commands. They can be registered as a 'Singleton' or 'Scoped'. 
        // 'Transient' will work but in practice it will behave the same as 'Scoped'.
        services.AddSingleton<CloneAndReplaceCommand>();
        services.AddSingleton<SetVariablesCommand>();
        services.AddSingleton<InsertInheritDocCommand>();
        services.AddSingleton<IDteUtil, DteUtil>();

        services.TryAddSingleton<ILoggerFactory, LoggerFactory>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(Logger<>));
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);
    }
}