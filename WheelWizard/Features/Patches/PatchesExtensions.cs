namespace WheelWizard.Features.Patches;

public static class PatchesExtensions
{
    public static IServiceCollection AddPatches(this IServiceCollection services)
    {
        services.AddSingleton<ISzsPatchConverter, SzsPatchConverter>();
        services.AddSingleton<IModPatchConversionService, ModPatchConversionService>();
        return services;
    }
}
