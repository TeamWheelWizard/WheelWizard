using System.Text.Json;
using Refit;

namespace WheelWizard.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWhWzRefitApi<T>(this IServiceCollection services, string baseAddress, JsonSerializerOptions? options = null)
        where T : class
    {
        services.AddRefitClient<T>(new()
        {
            ContentSerializer = new SystemTextJsonContentSerializer(options ?? JsonSerializerOptions.Default)
        }).ConfigureHttpClient((sp, client) =>
        {
            client.ConfigureWheelWizardClient(sp);

            client.BaseAddress = new(baseAddress);
        }).AddStandardResilienceHandler();

        return services;
    }
}
