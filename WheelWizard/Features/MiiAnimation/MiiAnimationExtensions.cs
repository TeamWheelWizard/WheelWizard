using WheelWizard.MiiAnimations.Library;

namespace WheelWizard.MiiAnimations;

public static class MiiAnimationExtensions
{
    public static IServiceCollection AddMiiAnimations(this IServiceCollection services)
    {
        services.AddSingleton<IMiiAnimationLibrary, MiiAnimationLibrary>();
        return services;
    }
}
