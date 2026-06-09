namespace WheelWizard.RrRooms;

public sealed class RwfcLeaderboardResponse
{
    public List<RwfcLeaderboardEntry> Players { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public int PageSize { get; set; }
    public RwfcLeaderboardStats? Stats { get; set; }
}

public sealed class RwfcLeaderboardStats
{
    public int TotalPlayers { get; set; }
    public int SuspiciousPlayers { get; set; }
    public DateTime LastUpdated { get; set; }
}

public sealed class RwfcLeaderboardRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? Search { get; set; }
    public string SortBy { get; set; } = "rank";
    public bool Ascending { get; set; } = true;
    public string TimePeriod { get; set; } = "24";
    public int? ActiveDays { get; set; }
}
