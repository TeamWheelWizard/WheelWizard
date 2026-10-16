using WheelWizard.GameFiles.Views;

namespace WheelWizard.GameFiles;

public static class GameFilesExtensions
{
    public static IServiceCollection AddGameFiles(this IServiceCollection services)
    {
        services.AddSingleton<IGameFilesPaths, GameFilesPaths>();
        services.AddSingleton<IDiscExtractor, DiscExtractor>();
        services.AddSingleton<IGameFilesService, GameFilesService>();
        services.AddSingleton<IGameFilesFlow, GameFilesFlow>();
        return services;
    }
}
