using System.IO.Abstractions;
using Testably.Abstractions;
using WheelWizard.AutoUpdating;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData;

namespace WheelWizard;

public static class SetupExtensions
{
    /// <summary>
    /// Adds the services required for WheelWizard.
    /// </summary>
    public static void AddWheelWizardServices(this IServiceCollection services)
    {
        services.AddAutoUpdating();
        services.AddBranding();
        services.AddGitHub();
        services.AddRrRooms();
        services.AddWhWzData();

        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<ITimeSystem, RealTimeSystem>();

        // Dynamic API calls
        services.AddTransient(typeof(IApiCaller<>), typeof(ApiCaller<>));
    }
}
