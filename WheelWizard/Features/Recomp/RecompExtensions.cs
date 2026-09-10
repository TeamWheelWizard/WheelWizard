using System.Net.Http.Headers;
using WheelWizard.Shared;

namespace WheelWizard.Recomp;

public static class RecompExtensions
{
    /// <summary>
    /// Registers the Mario Kart Wii recomp frontend.
    /// The recomp only ships for Windows and Linux, so on every other platform nothing is registered at
    /// all; <c>ISettingsManager.IsRecompModeActive()</c> is false there, so nothing ever resolves these.
    /// Both platforms share how a setup release is found and downloaded; what that setup is then told to
    /// do differs, so each gets its own environment and install service.
    /// </summary>
    public static IServiceCollection AddRecomp(this IServiceCollection services)
    {
        if (!RecompPlatform.IsSupported)
            return services;

        services
            .AddHttpClient(RecompSetupDownloader.HttpClientName)
            .ConfigureHttpClient(
                (serviceProvider, client) =>
                {
                    client.ConfigureWheelWizardClient(serviceProvider);

                    // GitHub release assets are served as an octet-stream redirect.
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                }
            );

        services.AddSingleton<IRecompDolphinDataService, RecompDolphinDataService>();
        services.AddSingleton<IRecompProcessRunner, RecompProcessRunner>();
        services.AddSingleton<IRecompSetupDownloader, RecompSetupDownloader>();
        services.AddSingleton<IRecompRetroWfcPayloadProbe, RecompRetroWfcPayloadProbe>();
        services.AddSingleton<RecompSetupHostAcquirer>();

        if (RecompPlatform.IsLinux)
        {
            services.AddSingleton<IRecompEnvironment, RecompLinuxEnvironment>();
            services.AddSingleton<RecompLinuxProductInspector>();
            services.AddSingleton<IRecompInstallService, RecompLinuxInstallService>();
        }
        else
        {
            services.AddSingleton<IRecompEnvironment, RecompEnvironment>();
            services.AddSingleton<IRecompInstallService, RecompInstallService>();
        }

        services.AddTransient<RecompLauncher>();

        return services;
    }
}
