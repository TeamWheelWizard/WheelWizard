using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering;

public static class MiiRenderingExtensions
{
    public static IServiceCollection AddMiiRendering(this IServiceCollection services)
    {
        services.AddSingleton(_ => MiiRenderingConfiguration.CreateDefault());
        services.AddSingleton<IMiiRenderingResourceLocator, MiiRenderingResourceLocator>();
        services.AddSingleton<IMiiNativeRenderer, NativeMiiRenderer>();
        return services;
    }
}
