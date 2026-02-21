namespace WheelWizard.Settings;

public static class SettingsExtensions
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        // TODO(naming-cleanup, later):
        // - Prefix casing is inconsistent: `WhWz*` vs `WW_*` (example: `WhWzSettingManager`, `WW_LANGUAGE`).
        // - Some setting identifiers use all-caps while others use PascalCase (example: `MACADDRESS` vs `GAME_LOCATION`).
        // - Domain type naming is mixed between generic and feature-specific terms (`Setting`, `WhWzSetting`, `DolphinSetting`).

        // Next step:
        // - we created Settings Singal bus, make sure it is also being used everywhere such that you dont subseribve or unsubscribe to a setting model anymore.
        // - Remove/ move the model to the settings add it in Domains. make sure there is no ugly remenance somewhere in the depricated codebase anymore
        // - i saw setting still use path manager, can we simply replace that iwt the IFileSystem? or does that need some more work
        // - investigate if localization service really has to be its own setting service or if it can be combined with the rest of the settings. OR maybe even better, that it can become its own localization feature (that uses setting manager)
        // - look at the settings initializer, and if we really need that or if there is a better way

        // look at all the code as high level overview

        services.AddSingleton<ISettingsSignalBus, SettingsSignalBus>();
        services.AddSingleton<IWhWzSettingManager, WhWzSettingManager>();
        services.AddSingleton<IDolphinSettingManager, DolphinSettingManager>();
        services.AddSingleton<ISettingsManager, SettingsManager>();
        services.AddSingleton<ISettingsLocalizationService, SettingsLocalizationService>();
        services.AddSingleton<ISettingsStartupInitializer, SettingsStartupInitializer>();

        return services;
    }
}
