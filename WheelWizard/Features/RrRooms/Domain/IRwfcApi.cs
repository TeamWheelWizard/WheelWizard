using Refit;

namespace WheelWizard.RrRooms;

public interface IRwfcApi
{
    [Get("/api/roomstatus")]
    Task<RwfcRoomStatusResponse> GetRoomStatusAsync();

    [Get("/api/leaderboard/top/{limit}")]
    Task<List<RwfcLeaderboardEntry>> GetTopLeaderboardAsync(int limit);
}
