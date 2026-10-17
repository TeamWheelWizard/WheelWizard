using WheelWizard.MiiRendering.FaceLibrary;
using WheelWizard.MiiRendering.Services;

namespace WheelWizard.MiiRendering;

public static class MiiRenderingExtensions
{
    public static IServiceCollection AddMiiRendering(this IServiceCollection services)
    {
        services.AddSingleton<IMiiFaceLibrary, MiiFaceLibrary>();
        services.AddSingleton<IMiiNativeRenderer, NativeMiiRenderer>();
        return services;
    }
}
