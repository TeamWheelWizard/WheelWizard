using WheelWizard.GameBanana.Domain;
using WheelWizard.Services;

namespace WheelWizard.GameBanana;

public static class GameBananaExtensions
{
    public static IServiceCollection AddGameBanana(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IGameBananaApi>(Endpoints.GameBananaBaseAddress);
        services.AddSingleton<IGameBananaSingletonService, GameBananaSingletonService>();
        services.AddHttpClient(
            GameBananaMediaService.ClientName,
            (provider, client) =>
            {
                client.ConfigureWheelWizardClient(provider);
                client.Timeout = TimeSpan.FromSeconds(6);
            }
        );
        services.AddSingleton<IGameBananaMediaService, GameBananaMediaService>();
        services.AddSingleton<InstallRequests.IModInstallRequestHandler, InstallRequests.ModInstallRequestHandler>();
        return services;
    }
}
