using WheelWizard.Services;

namespace WheelWizard;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds the services required for WheelWizard.
    /// </summary>
    public static void AddWheelWizardServices(this IServiceCollection services)
    {
        services.AddSingleton<IBadgeSingletonService, BadgeSingletonService>();
    }
}
