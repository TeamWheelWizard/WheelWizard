using Microsoft.Extensions.DependencyInjection.Extensions;

namespace WheelWizard.Shared.Downloads;

public static class DownloadExtensions
{
    public static IServiceCollection AddDownloads(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new DownloadOptions());
        services.AddHttpClient(
            DownloadService.ClientName,
            (provider, client) =>
            {
                client.ConfigureWheelWizardClient(provider);
                client.Timeout = TimeSpan.FromSeconds(30);
            }
        );
        services.AddSingleton<IDownloadService, DownloadService>();
        return services;
    }
}
