using System.Net.Http.Headers;
using Refit;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Domain;
using WheelWizard.MiiRendering.Services;
using WheelWizard.Services;

namespace WheelWizard.MiiRendering;

public static class MiiRenderingExtensions
{
    public static IServiceCollection AddMiiRendering(this IServiceCollection services)
    {
        services
            .AddRefitClient<IMiiRenderingAssetApi>()
            .ConfigureHttpClient(client =>
            {
                client.BaseAddress = new Uri(Endpoints.InternetArchiveBaseAddress);
                client.DefaultRequestHeaders.UserAgent.Clear();
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/133.0.0.0 Safari/537.36"
                );
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/zip"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.8));
            })
            .AddStandardResilienceHandler();
        services.AddSingleton(_ => MiiRenderingConfiguration.CreateDefault());
        services.AddSingleton<IMiiRenderingResourceLocator, MiiRenderingResourceLocator>();
        services.AddSingleton<IMiiRenderingResourceInstaller, MiiRenderingResourceInstaller>();
        services.AddSingleton<IMiiNativeRenderer, NativeMiiRenderer>();
        return services;
    }
}
