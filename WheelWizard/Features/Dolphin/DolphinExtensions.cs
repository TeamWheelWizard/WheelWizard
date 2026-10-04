using Microsoft.Extensions.DependencyInjection.Extensions;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Dolphin;

public static class DolphinExtensions
{
    public static IServiceCollection AddDolphin(this IServiceCollection services)
    {
        services.TryAddSingleton<IRuntimeEnvironment, RuntimeEnvironment>();
        services.TryAddSingleton<IDolphinPaths, DolphinPaths>();
        services.TryAddSingleton<IDolphinPathResolver, DolphinPathResolver>();
        services.TryAddSingleton<IDolphinRegistrySettings, DolphinRegistrySettings>();
        services.TryAddSingleton<IDolphinDiscoveryService, DolphinDiscoveryService>();
        return services;
    }
}
