using System.IO.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Testably.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.AutoUpdating;
using WheelWizard.Branding;
using WheelWizard.CustomCharacters;
using WheelWizard.CustomDistributions;
using WheelWizard.DolphinInstaller;
using WheelWizard.Features.Archives;
using WheelWizard.Features.Patches;
using WheelWizard.GameBanana;
using WheelWizard.GitHub;
using WheelWizard.Localization;
using WheelWizard.MiiImages;
using WheelWizard.Mods;
using WheelWizard.Recomp;
using WheelWizard.RrRooms;
using WheelWizard.Services.Launcher;
using WheelWizard.Settings;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;
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
        services.AddSingleton<WheelWizard.Shared.Polling.IPollingScheduler, WheelWizard.Views.Polling.AvaloniaPollingScheduler>();
        services.AddSingleton<WheelWizard.Views.Diagnostics.DevelopmentRefreshService>();
        // Features
        services.AddSingleton<WheelWizard.Shared.Processes.IUnixCommandService, WheelWizard.Shared.Processes.UnixCommandService>();
        services.AddSingleton<WheelWizard.Shared.Processes.IUnixProcessService, WheelWizard.Shared.Processes.UnixProcessService>();
        services.AddDolphinInstaller();
        services.AddSingleton<WheelWizard.Launching.IRetroRewindLaunchDescriptor, WheelWizard.Launching.RetroRewindLaunchDescriptor>();
        services.AddSingleton<WheelWizard.Shared.Processes.IProcessLauncher, WheelWizard.Shared.Processes.ProcessLauncher>();
        services.AddSingleton<WheelWizard.Launching.IDolphinLaunchService, WheelWizard.Launching.DolphinLaunchService>();
        services.AddSingleton<WheelWizard.Launching.IDolphinLaunchPresentation, WheelWizard.Views.Launching.DolphinLaunchPresentation>();
        services.AddDownloads();
        services.AddTransient<WheelWizard.Launching.MiiChannelLauncher>();
        services.AddLocalization();
        services.AddSettings();
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
        services.AddArchives();
        services.AddPatches();
        services.AddMods();
        services.AddRecomp();

        services.AddSingleton<IApplicationDataLocation>(Services.PathManager.ApplicationData);

        // IO Abstractions
        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<IDirectoryTransferService, DirectoryTransferService>();
        services.AddSingleton<ITimeSystem, RealTimeSystem>();
        services.AddSingleton<IRandomSystem, RealRandomSystem>();
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));

        // Logging
        services.AddTransient<AvaloniaLoggerAdapter>();
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));

        // Dynamic API calls
        services.AddTransient(typeof(IApiCaller<>), typeof(ApiCaller<>));
        services.AddTransient<RrLauncher>();
        services.AddTransient<RrBetaLauncher>();
        services.AddSingleton<ILauncherProvider, LauncherProvider>();
    }
}
