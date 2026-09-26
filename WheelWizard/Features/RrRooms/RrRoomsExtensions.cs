using System.Text.Json;
using WheelWizard.Services;

namespace WheelWizard.RrRooms;

public static class RrRoomsExtensions
{
    public static IServiceCollection AddRrRooms(this IServiceCollection services)
    {
        services.AddWhWzRefitApi<IRwfcApi>(
            Endpoints.RwfcBaseAddress,
            new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        services.AddSingleton<IRrRoomsSingletonService, RrRoomsSingletonService>();
        services.AddSingleton<IRrLeaderboardSingletonService, RrLeaderboardSingletonService>();

        services.AddSingleton<LiveRoomsService>();
        services.AddSingleton<IRoomPresence, RoomPresence>();

        return services;
    }
}
