using WheelWizard.AutoUpdating.Platforms;

namespace WheelWizard.AutoUpdating;

public static class AutoUpdatingExtensions
{
    public static IServiceCollection AddAutoUpdating(this IServiceCollection services)
    {
        services.AddSingleton<IAutoUpdaterSingletonService, AutoUpdaterSingletonService>();
        services.AddSingleton<IBundleExtractionCleanupService, BundleExtractionCleanupService>();

        var implementationType = typeof(FallbackUpdatePlatform);
#if WINDOWS
        implementationType = typeof(WindowsUpdatePlatform);
#elif LINUX
        // We can enable this again once the auto updater has been fixed and tested
        implementationType = typeof(LinuxUpdatePlatform);
#elif MACOS
        // MacOS updater
#endif

        services.AddSingleton(typeof(IUpdatePlatform), implementationType);

        return services;
    }
}
