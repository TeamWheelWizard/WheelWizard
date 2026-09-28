namespace WheelWizard.Localization;

public static class LocalizationExtensions
{
    public static IServiceCollection AddLocalization(this IServiceCollection services)
    {
        services.AddSingleton<ILocalizationService, EmbeddedYamlLocalizationService>();

        return services;
    }
}
