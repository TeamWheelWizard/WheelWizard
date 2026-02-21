namespace WheelWizard.Settings;

public static class SettingsExtensions
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        // TODO(naming-cleanup):
        // - Prefix casing is inconsistent: `WhWz*` vs `WW_*` (example: `WhWzSettingManager`, `WW_LANGUAGE`).
        // - Some setting identifiers use all-caps while others use PascalCase (example: `MACADDRESS` vs `GAME_LOCATION`).
        // - Domain type naming is mixed between generic and feature-specific terms (`Setting`, `WhWzSetting`, `DolphinSetting`).

       // TODO:  Investigate / migrate to IOptions: https://learn.microsoft.com/en-us/dotnet/core/extensions/options

        services.AddSingleton<ISettingsSignalBus, SettingsSignalBus>();
        services.AddSingleton<IWhWzSettingManager, WhWzSettingManager>();
        services.AddSingleton<IDolphinSettingManager, DolphinSettingManager>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<ISettingsLocalizationService, SettingsLocalizationService>();
        services.AddSingleton<ISettingsStartupInitializer, SettingsStartupInitializer>();

        return services;
    }
}
