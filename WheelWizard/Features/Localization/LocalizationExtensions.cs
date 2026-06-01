namespace WheelWizard.Localization;

public static class LocalizationExtensions
{
    public static IServiceCollection AddLocalization(this IServiceCollection services)
    {
        services.AddSingleton<ILocalizationService>(_ =>
        {
            var service = new EmbeddedYamlLocalizationService();
            LocalizationProvider.Use(service);
            return service;
        });

        return services;
    }
}
