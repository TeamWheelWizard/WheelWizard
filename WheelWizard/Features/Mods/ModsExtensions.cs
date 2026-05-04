namespace WheelWizard.Mods;

public static class ModsExtensions
{
    public static IServiceCollection AddMods(this IServiceCollection services)
    {
        services.AddSingleton<IModInstallationService, ModInstallationService>();
        services.AddSingleton<IModManager, ModManager>();
        services.AddSingleton<IModsLaunchService, ModsLaunchService>();
        return services;
    }
}
