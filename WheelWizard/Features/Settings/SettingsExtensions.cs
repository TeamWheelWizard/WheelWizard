namespace WheelWizard.Settings;

public static class SettingsExtensions
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddSingleton<IWhWzSettingManager, WhWzSettingManager>();
        services.AddSingleton<IDolphinSettingManager, DolphinSettingManager>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<ISettingsStartupInitializer, SettingsStartupInitializer>();

        return services;
    }
}
