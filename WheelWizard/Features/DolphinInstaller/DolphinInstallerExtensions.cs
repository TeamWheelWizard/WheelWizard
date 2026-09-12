using WheelWizard.Shared.Processes;

namespace WheelWizard.DolphinInstaller;

public static class DolphinInstallerExtensions
{
    public static IServiceCollection AddDolphinInstaller(this IServiceCollection services)
    {
        services.AddSingleton<ILinuxDolphinInstaller, LinuxDolphinInstaller>();
        services.AddSingleton<IDolphinVersionService, DolphinVersionService>();
        return services;
    }
}
