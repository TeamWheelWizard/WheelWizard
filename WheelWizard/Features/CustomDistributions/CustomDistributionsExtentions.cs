using Refit;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Services;

namespace WheelWizard.CustomDistributions;

public static class CustomDistributionsExtentions
{
    public static IServiceCollection AddCustomDistributionService(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IRetroRewindApi>(Endpoints.RRUrl);

        services.AddSingleton<ICustomDistributionSingletonService, CustomDistributionSingletonService>();
        return services;
    }
}
