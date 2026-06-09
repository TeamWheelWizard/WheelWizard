namespace WheelWizard.RrRooms;

public sealed class RwfcPlayerRaceStatsResponse
{
    public int TotalRaces { get; set; }
    public DateTime TrackedSince { get; set; }
    public List<RwfcTrackPlayCount> TopTracks { get; set; } = [];
    public List<RwfcSetupEntry> TopCharacters { get; set; } = [];
    public List<RwfcSetupEntry> TopVehicles { get; set; } = [];
    public List<RwfcSetupEntry> TopCombos { get; set; } = [];
    public long TotalFramesIn1st { get; set; }
    public double AvgFramesIn1stPerRace { get; set; }
    public List<RwfcRecentRace> RecentRaces { get; set; } = [];
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalRecentRaces { get; set; }
    public List<RwfcSetupWinRateEntry> TopCharactersByWinRate { get; set; } = [];
    public List<RwfcSetupWinRateEntry> TopVehiclesByWinRate { get; set; } = [];
    public List<RwfcSetupWinRateEntry> TopCombosByWinRate { get; set; } = [];
    public List<RwfcSetupWinRateEntry> TopCharactersByWinCount { get; set; } = [];
    public List<RwfcSetupWinRateEntry> TopVehiclesByWinCount { get; set; } = [];
    public List<RwfcSetupWinRateEntry> TopCombosByWinCount { get; set; } = [];
}

public sealed class RwfcTrackPlayCount
{
    public string TrackName { get; set; } = string.Empty;
    public int RaceCount { get; set; }
    public short CourseId { get; set; }
}

public sealed class RwfcSetupEntry
{
    public string Name { get; set; } = string.Empty;
    public int RaceCount { get; set; }
}

public sealed class RwfcSetupWinRateEntry
{
    public string Name { get; set; } = string.Empty;
    public int RaceCount { get; set; }
    public int WinCount { get; set; }
    public double WinRate { get; set; }
}

public sealed class RwfcRecentRace
{
    public string TrackName { get; set; } = string.Empty;
    public short CourseId { get; set; }
    public string FinishTimeDisplay { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public string VehicleName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public short? FinishPos { get; set; }
    public short PlayerCount { get; set; }
    public string RoomId { get; set; } = string.Empty;
    public int RaceNumber { get; set; }
    public string? GameMode { get; set; }
    public bool? IsPublic { get; set; }
}
