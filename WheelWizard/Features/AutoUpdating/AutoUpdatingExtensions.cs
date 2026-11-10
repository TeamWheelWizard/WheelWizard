using System.IO.Abstractions;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Shared.Platform;

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
        implementationType = typeof(LinuxUpdatePlatform);
#elif MACOS
        // MacOS updater
#endif

        services.AddSingleton<IUpdatePlatform>(provider =>
        {
            // Flatpak owns its executable even when its build defines LINUX.
            var platform = provider.GetRequiredService<IRuntimeEnvironment>().IsFlatpakSandboxed(provider.GetRequiredService<IFileSystem>())
                ? typeof(FallbackUpdatePlatform)
                : implementationType;
            return (IUpdatePlatform)ActivatorUtilities.CreateInstance(provider, platform);
        });

        return services;
    }
}
