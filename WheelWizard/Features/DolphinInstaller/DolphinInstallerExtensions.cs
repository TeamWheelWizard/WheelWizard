using WheelWizard.Shared.Processes;

namespace WheelWizard.DolphinInstaller;

public static class DolphinInstallerExtensions
{
    public static IServiceCollection AddDolphinInstaller(this IServiceCollection services)
    {
        services.AddSingleton<ILinuxDolphinInstaller, LinuxDolphinInstaller>();
        services.AddSingleton<IDolphinVersionService, DolphinVersionService>();
        services.AddSingleton<IDolphinReleaseClient, DolphinReleaseClient>();
        services.AddSingleton<IManagedDolphinService, ManagedDolphinService>();
        return services;
    }
}
