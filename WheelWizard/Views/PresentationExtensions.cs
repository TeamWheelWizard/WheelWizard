using WheelWizard.AutoUpdating;
using WheelWizard.CustomDistributions;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Launching;
using WheelWizard.Mods;
using WheelWizard.Recomp;
using WheelWizard.Shared.Polling;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Diagnostics;
using WheelWizard.Views.Distributions;
using WheelWizard.Views.Launching;
using WheelWizard.Views.ModManagement;
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Patterns;
using WheelWizard.Views.Polling;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.ModManagement;
using WheelWizard.Views.Recomp;
using WheelWizard.Views.Startup;
using WheelWizard.Views.Storage;
using WheelWizard.Views.Updating;

namespace WheelWizard.Views;

public static class PresentationExtensions
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<IPollingScheduler, AvaloniaPollingScheduler>();
        services.AddSingleton<DevelopmentRefreshService>();
        services.AddSingleton<IModInstallRequestPresentation, ModInstallRequestPresentation>();
        services.AddSingleton<IStorageProviderAccessor, DesktopStorageProviderAccessor>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<IDistributionPrompts, DistributionPrompts>();
        services.AddSingleton<IDolphinLaunchPresentation, DolphinLaunchPresentation>();
        services.AddSingleton<IUpdatePresentation, UpdatePresentation>();
        services.AddSingleton<IRecompPresentation, RecompPresentation>();
        services.AddTransient<HomeViewModel>();
        services.AddSingleton<IHomePresentation, HomePresentation>();
        services.AddTransient<Func<int, ModPreviewViewModel>>(provider =>
        {
            var mods = provider.GetRequiredService<IGameBananaSingletonService>();
            var media = provider.GetRequiredService<IGameBananaMediaService>();
            return id => new ModPreviewViewModel(id, mods, media);
        });
        services.AddSingleton<IPageFactory, PageFactory>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IPopupFactory, PopupFactory>();
        services.AddTransient<ModContent>();
        services.AddTransient<VrHistoryViewModel>();
        services.AddTransient<VrHistoryGraph>();
        services.AddSingleton<MiiControlThemes>();
        services.AddTransient<Layout>();
        services.AddSingleton<Func<Layout>>(provider => () => provider.GetRequiredService<Layout>());
        services.AddSingleton<IMainWindowService, MainWindowService>();
        services.AddSingleton<WindowAppearance>();
        services.AddSingleton<IDesktopStartup, DesktopStartup>();
        services.AddSingleton<IMiiSetupPresentation, MiiSetupPresentation>();
        services.AddSingleton<IModOperationPresentation, ModOperationPresentation>();
        services.AddTransient<AvaloniaLoggerAdapter>();
        services.AddSingleton<ILaunchPrompts, LaunchPrompts>();
        services.AddSingleton<IDistributionOperationPresentation, DistributionOperationPresentation>();
        return services;
    }
}
