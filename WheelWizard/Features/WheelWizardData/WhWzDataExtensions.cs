using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Services;
using WheelWizard.Shared.JsonConverters;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public static class WhWzDataExtensions
{
    public static IServiceCollection AddWhWzData(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IWhWzDataApi>(
            Endpoints.WhWzDataBaseAddress,
            new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                Converters = { new EnumWithFallbackConverter<BadgeVariant>(), new JsonStringEnumConverter() },
            }
        );

        services.AddSingleton<IWhWzDataSingletonService, WhWzDataSingletonService>();

        services.AddSingleton<LiveStatusService>();

        return services;
    }
}
