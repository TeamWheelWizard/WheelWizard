using WheelWizard.Shared.Services;

namespace WheelWizard.RrRooms;

public interface IRrLeaderboardSingletonService
{
    Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50);
}

public class RrLeaderboardSingletonService(IApiCaller<IRwfcApi> apiCaller) : IRrLeaderboardSingletonService
{
    public async Task<OperationResult<List<RwfcLeaderboardEntry>>> GetTopPlayersAsync(int limit = 50)
    {
        var boundedLimit = Math.Clamp(limit, 1, 200);
        return await apiCaller.CallApiAsync(api => api.GetTopLeaderboardAsync(boundedLimit));
    }
}
