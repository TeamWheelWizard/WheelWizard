using WheelWizard.DolphinManagent.Abstractions;
using WheelWizard.DolphinManagent.Linux;

namespace WheelWizard.DolphinManagent;

public static class DolphinManagmentExtensions
{
    public static IServiceCollection AddDolphinManagement(this IServiceCollection services)
    {
#if LINUX
        services.AddSingleton<ILinuxCommandEnvironment, LinuxCommandEnvironment>();
        services.AddSingleton<ILinuxProcessService, LinuxProcessService>();
        //services.AddSingleton<IDolphinInstaller, LinuxDolphinInstaller>();
        services.AddSingleton<IDolphinLocator, LinuxDolphinLocator>();
#endif
        return services;
    }
}
