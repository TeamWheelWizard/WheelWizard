using System.IO.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Testably.Abstractions;
using WheelWizard.AutoUpdating;
using WheelWizard.Branding;
using WheelWizard.CustomCharacters;
using WheelWizard.CustomDistributions;
using WheelWizard.GameBanana;
using WheelWizard.GitHub;
using WheelWizard.MiiImages;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard;

public static class SetupExtensions
{
    /// <summary>
    /// Adds the services required for WheelWizard.
    /// </summary>
    public static void AddWheelWizardServices(this IServiceCollection services)
    {
        // Features
        services.AddCustomCharacters();
        services.AddAutoUpdating();
        services.AddBranding();
        services.AddGitHub();
        services.AddRrRooms();
        services.AddWhWzData();
        services.AddWiiManagement();
        services.AddGameBanana();
        services.AddMiiImages();
        services.AddCustomDistributionService();

        // IO Abstractions
        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<ITimeSystem, RealTimeSystem>();
        services.AddSingleton<IRandomSystem, RealRandomSystem>();
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));

        // Logging
        services.AddTransient<AvaloniaLoggerAdapter>();
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        // Dynamic API calls
        services.AddTransient(typeof(IApiCaller<>), typeof(ApiCaller<>));
    }
}
