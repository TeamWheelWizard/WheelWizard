using System.Text.Json;
using System.Text.Json.Serialization;
using WheelWizard.Shared.JsonConverters;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public static class WhWzDataExtensions
{
    public static IServiceCollection AddWhWzData(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IWhWzDataApi>(
            "https://raw.githubusercontent.com/TeamWheelWizard/WheelWizard-Data/main",
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
