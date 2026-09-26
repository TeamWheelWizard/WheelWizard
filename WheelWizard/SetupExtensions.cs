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
using WheelWizard.Launching;
using WheelWizard.Localization;
using WheelWizard.MiiImages;
using WheelWizard.Mods;
using WheelWizard.Recomp;
using WheelWizard.RrRooms;
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
    public static void AddWheelWizardServices(this IServiceCollection services, IApplicationDataLocation? applicationData = null)
    {
        services.AddSingleton<WheelWizard.Shared.Polling.IPollingScheduler, WheelWizard.Views.Polling.AvaloniaPollingScheduler>();
        services.AddSingleton<WheelWizard.Views.Diagnostics.DevelopmentRefreshService>();
        services.AddSingleton<
            WheelWizard.ApplicationIntegration.IUrlProtocolRegistrationStore,
            WheelWizard.ApplicationIntegration.WindowsUrlProtocolRegistrationStore
        >();
        services.AddSingleton<
            WheelWizard.ApplicationIntegration.IUrlProtocolRegistration,
            WheelWizard.ApplicationIntegration.UrlProtocolRegistration
        >();
        services.AddSingleton<
            WheelWizard.GameBanana.InstallRequests.IModInstallRequestPresentation,
            WheelWizard.Views.ModManagement.ModInstallRequestPresentation
        >();
        services.AddSingleton<
            WheelWizard.Views.Storage.IStorageProviderAccessor,
            WheelWizard.Views.Storage.DesktopStorageProviderAccessor
        >();
        services.AddSingleton<WheelWizard.Views.Storage.IFilePickerService, WheelWizard.Views.Storage.FilePickerService>();
        services.AddSingleton<WheelWizard.CustomDistributions.IDistributionPrompts, WheelWizard.Views.Distributions.DistributionPrompts>();
        // Features
        services.AddSingleton<WheelWizard.Shared.Processes.IUnixCommandService, WheelWizard.Shared.Processes.UnixCommandService>();
        services.AddSingleton<WheelWizard.Shared.Processes.IUnixProcessService, WheelWizard.Shared.Processes.UnixProcessService>();
        services.AddDolphinInstaller();
        services.AddSingleton<WheelWizard.Launching.IRetroRewindLaunchDescriptor, WheelWizard.Launching.RetroRewindLaunchDescriptor>();
        services.AddSingleton<WheelWizard.Shared.Processes.IProcessLauncher, WheelWizard.Shared.Processes.ProcessLauncher>();
        services.AddSingleton<WheelWizard.Launching.IDolphinLaunchService, WheelWizard.Launching.DolphinLaunchService>();
        services.AddSingleton<WheelWizard.Launching.IDolphinLaunchPresentation, WheelWizard.Views.Launching.DolphinLaunchPresentation>();
        services.AddDownloads();
        services.AddLocalization();
        services.AddSettings();
        services.AddCustomCharacters();
        services.AddAutoUpdating();
        services.AddSingleton<WheelWizard.AutoUpdating.IUpdatePresentation, WheelWizard.Views.Updating.UpdatePresentation>();
        services.AddSingleton<WheelWizard.Shared.Processes.IApplicationProcess, WheelWizard.Shared.Processes.ApplicationProcess>();
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
        services.AddSingleton<WheelWizard.Recomp.IRecompPresentation, WheelWizard.Views.Recomp.RecompPresentation>();
        services.AddSingleton<WheelWizard.Shared.Calendar.ISeasonalCalendar, WheelWizard.Shared.Calendar.SeasonalCalendar>();
        services.AddTransient<WheelWizard.Views.Pages.HomeViewModel>();
        services.AddSingleton<WheelWizard.Views.Pages.IHomePresentation, WheelWizard.Views.Pages.HomePresentation>();
        services.AddTransient<Func<int, WheelWizard.Views.ModManagement.ModPreviewViewModel>>(provider =>
        {
            var mods = provider.GetRequiredService<WheelWizard.GameBanana.IGameBananaSingletonService>();
            var media = provider.GetRequiredService<WheelWizard.GameBanana.IGameBananaMediaService>();
            return id => new WheelWizard.Views.ModManagement.ModPreviewViewModel(id, mods, media);
        });
        services.AddMods();
        services.AddSingleton<IModOperationPresentation, WheelWizard.Views.ModManagement.ModOperationPresentation>();
        services.AddRecomp();

        if (applicationData != null)
            services.AddSingleton(applicationData);
        else
            services.AddSingleton(provider =>
                ApplicationDataComposition.CreateLocation(
                    provider.GetRequiredService<IFileSystem>(),
                    provider.GetRequiredService<WheelWizard.Shared.Platform.IRuntimeEnvironment>()
                )
            );

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
        services.AddSingleton<IRetroRewindLaunchService, RetroRewindLaunchService>();
        services.AddSingleton<ILaunchPrompts, WheelWizard.Views.Launching.LaunchPrompts>();
        services.AddSingleton<IDistributionOperationPresentation, WheelWizard.Views.Distributions.DistributionOperationPresentation>();
        services.AddSingleton<Func<RrLauncher>>(provider => () => provider.GetRequiredService<RrLauncher>());
        services.AddSingleton<Func<RecompLauncher?>>(provider => () => provider.GetService<RecompLauncher>());
        services.AddTransient<RrLauncher>();
        services.AddTransient<RrBetaLauncher>();
        services.AddSingleton<ILauncherProvider, LauncherProvider>();
    }
}
