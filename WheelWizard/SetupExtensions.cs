using WheelWizard.ApplicationData;
using WheelWizard.ApplicationIntegration;
using WheelWizard.ApplicationLifecycle;
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
using WheelWizard.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;

namespace WheelWizard;

public static class SetupExtensions
{
    /// <summary>Composes feature services and their desktop presentation adapters.</summary>
    public static void AddWheelWizardServices(this IServiceCollection services, IApplicationDataLocation? applicationData = null)
    {
        services.AddSharedServices();
        services.AddApplicationData(applicationData);
        services.AddApplicationIntegration();
        services.AddApplicationLifecycle();
        services.AddLaunching();
        services.AddDolphinInstaller();
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
        services.AddPresentation();
    }
}
