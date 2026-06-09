using Refit;
using WheelWizard.Models;

namespace WheelWizard.RrRooms;

public interface IRwfcApi
{
    [Get("/api/roomstatus")]
    Task<RwfcRoomStatusResponse> GetRoomStatusAsync();

    [Get("/api/leaderboard/top/{limit}")]
    Task<List<RwfcLeaderboardEntry>> GetTopLeaderboardAsync(int limit);

    [Get("/api/leaderboard")]
    Task<RwfcLeaderboardResponse> GetLeaderboardAsync([Query] RwfcLeaderboardRequest request);

    [Get("/api/leaderboard/player/{friendCode}")]
    Task<PlayerProfileResponse> GetPlayerProfileAsync(string friendCode);

    [Get("/api/leaderboard/player/{friendCode}/history")]
    Task<RwfcPlayerVrHistoryResponse> GetPlayerVrHistoryAsync(string friendCode, [AliasAs("days")] int days);

    [Get("/api/racestats/player/{pid}")]
    Task<RwfcPlayerRaceStatsResponse> GetPlayerRaceStatsAsync(
        string pid,
        [AliasAs("page")] int page = 1,
        [AliasAs("pageSize")] int pageSize = 8
    );
}
