namespace WheelWizard.DolphinInstaller;

public static class DolphinInstallerExtensions
{
    public static IServiceCollection AddDolphinInstaller(this IServiceCollection services)
    {
        // TODO: Reorganize this feature boundary.
        // Right now this registers 3 service concerns:
        // 1) Linux command environment, 2) Linux process execution, 3) Dolphin installer orchestration.
        // Consider either:
        // - moving Linux command/process services into a shared Linux feature/module, or
        // - using a strategy-based installer (like AutoUpdater) with platform/version-specific installer implementations.
        services.AddSingleton<ILinuxCommandEnvironment, LinuxCommandEnvironment>();
        services.AddSingleton<ILinuxProcessService, LinuxProcessService>();
        services.AddSingleton<ILinuxDolphinInstaller, LinuxDolphinInstaller>();
        return services;
    }
}
