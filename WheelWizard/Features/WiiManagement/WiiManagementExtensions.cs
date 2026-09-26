using WheelWizard.WiiManagement.Controllers;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.WiiManagement;

public static class WiiManagementExtensions
{
    public static IServiceCollection AddWiiManagement(this IServiceCollection services)
    {
        services.AddSingleton<IWiiRemoteConfigurationService, WiiRemoteConfigurationService>();
        services.AddSingleton<ISaveRegionService, SaveRegionService>();
        services.AddSingleton<IRrRatingReader, RrRatingReader>();
        services.AddSingleton<IMiiDbService, MiiDbService>();
        services.AddSingleton<IMiiRepositoryService, MiiRepositoryServiceService>();
        services.AddSingleton<IGameLicenseSingletonService, GameLicenseSingletonService>();
        return services;
    }
}
