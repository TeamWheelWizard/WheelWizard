using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Services;

namespace WheelWizard.MiiRendering;

public static class MiiRenderingExtensions
{
    public static IServiceCollection AddMiiRendering(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IMiiRenderingAssetApi>(Endpoints.InternetArchiveBaseAddress);
        services.AddSingleton(_ => MiiRenderingConfiguration.CreateDefault());
        services.AddSingleton<IMiiRenderingResourceLocator, MiiRenderingResourceLocator>();
        services.AddSingleton<IMiiRenderingResourceInstaller, MiiRenderingResourceInstaller>();
        services.AddSingleton<IMiiNativeRenderer, NativeMiiRenderer>();
        return services;
    }
}
