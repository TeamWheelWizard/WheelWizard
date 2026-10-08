using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WheelWizard.Shared;

namespace WheelWizard.Recomp;

public static class RecompExtensions
{
    /// <summary>
    /// Registers the Mario Kart Wii recomp frontend.
    /// The recomp only runs on Windows, Linux and macOS, so on every other platform nothing is registered
    /// at all; <c>ISettingsManager.IsRecompModeActive()</c> is false elsewhere, so nothing ever resolves
    /// these. Windows uses the v1 setup contract; Linux and macOS share the subcommand setup, so they share
    /// an environment and install service.
    /// </summary>
    public static IServiceCollection AddRecomp(this IServiceCollection services)
    {
        // Settings still need a config location on platforms without the recomp frontend.
        services.TryAddSingleton<IRecompPaths, RecompPaths>();
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

        if (RecompPlatform.UsesSubcommandSetup)
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
