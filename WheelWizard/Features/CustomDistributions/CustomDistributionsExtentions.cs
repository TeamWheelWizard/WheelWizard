namespace WheelWizard.CustomDistributions;

public static class CustomDistributionsExtentions
{
    public static IServiceCollection AddCustomDistributionService(this IServiceCollection services)
    {
        services.AddSingleton<ICustomDistributionSingletonService, CustomDistributionSingletonService>();
        return services;
    }
}
