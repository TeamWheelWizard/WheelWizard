using WheelWizard.AutoUpdating;
using WheelWizard.AutoUpdating.Presentation;
using WheelWizard.CustomDistributions;
using WheelWizard.CustomDistributions.Presentation;
using WheelWizard.GameBanana;
using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Launching;
using WheelWizard.Launching.Presentation;
using WheelWizard.MiiImages.Views;
using WheelWizard.Mods;
using WheelWizard.Mods.Presentation;
using WheelWizard.Mods.ViewModels;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Presentation;
using WheelWizard.Shared.Desktop.Polling;
using WheelWizard.Shared.Desktop.Storage;
using WheelWizard.Shared.Polling;
using WheelWizard.Shared.Services;
using WheelWizard.Views.DesignTime.Diagnostics;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.Presentation;
using WheelWizard.Views.Shell.Startup;
using WheelWizard.Views.Shell.ViewModels;
using WheelWizard.WheelWizardData.ViewModels;
using WheelWizard.WheelWizardData.Views;

namespace WheelWizard.Views.Shell;

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
