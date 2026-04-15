using System.Text.Json.Serialization;

namespace WheelWizard.Models;

public class GhostLeaderboardResponse
{
    [JsonPropertyName("track")]
    public GhostTrackInfo Track { get; set; } = new();
    
    [JsonPropertyName("cc")]
    public int Cc { get; set; }
    
    [JsonPropertyName("glitchAllowed")]
    public bool GlitchAllowed { get; set; }
    
    [JsonPropertyName("shroomless")]
    public bool? Shroomless { get; set; }
    
    [JsonPropertyName("vehicleFilter")]
    public string? VehicleFilter { get; set; }
    
    [JsonPropertyName("isFlap")]
    public bool IsFlap { get; set; }
    
    [JsonPropertyName("submissions")]
    public List<GhostSubmission> Submissions { get; set; } = new();
    
    [JsonPropertyName("totalSubmissions")]
    public int TotalSubmissions { get; set; }
    
    [JsonPropertyName("currentPage")]
    public int CurrentPage { get; set; }
    
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
    
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }
    
    [JsonPropertyName("fastestLapMs")]
    public int? FastestLapMs { get; set; }
    
    [JsonPropertyName("fastestLapDisplay")]
    public string FastestLapDisplay { get; set; } = string.Empty;
}

public class GhostTrackInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("courseId")]
    public int CourseId { get; set; }
    
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;
    
    [JsonPropertyName("laps")]
    public int Laps { get; set; }
    
    [JsonPropertyName("supportsGlitch")]
    public bool SupportsGlitch { get; set; }
    
    [JsonPropertyName("sortOrder")]
    public int SortOrder { get; set; }
}

public class GhostSubmission
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("trackId")]
    public int TrackId { get; set; }
    
    [JsonPropertyName("trackName")]
    public string TrackName { get; set; } = string.Empty;
    
    [JsonPropertyName("ttProfileId")]
    public int TtProfileId { get; set; }
    
    [JsonPropertyName("playerName")]
    public string PlayerName { get; set; } = string.Empty;
    
    [JsonPropertyName("countryCode")]
    public int CountryCode { get; set; }
    
    [JsonPropertyName("countryAlpha2")]
    public string? CountryAlpha2 { get; set; }
    
    [JsonPropertyName("countryName")]
    public string? CountryName { get; set; }
    
    [JsonPropertyName("cc")]
    public int Cc { get; set; }
    
    [JsonPropertyName("finishTimeMs")]
    public int FinishTimeMs { get; set; }
    
    [JsonPropertyName("finishTimeDisplay")]
    public string FinishTimeDisplay { get; set; } = string.Empty;
    
    [JsonPropertyName("vehicleId")]
    public int VehicleId { get; set; }
    
    [JsonPropertyName("characterId")]
    public int CharacterId { get; set; }
    
    [JsonPropertyName("controllerType")]
    public int ControllerType { get; set; }
    
    [JsonPropertyName("driftType")]
    public int DriftType { get; set; }
    
    [JsonPropertyName("shroomless")]
    public bool Shroomless { get; set; }
    
    [JsonPropertyName("glitch")]
    public bool Glitch { get; set; }
    
    [JsonPropertyName("isFlap")]
    public bool IsFlap { get; set; }
    
    [JsonPropertyName("driftCategory")]
    public int DriftCategory { get; set; }
    
    [JsonPropertyName("miiName")]
    public string MiiName { get; set; } = string.Empty;
    
    [JsonPropertyName("lapCount")]
    public int LapCount { get; set; }
    
    [JsonPropertyName("lapSplitsMs")]
    public List<int> LapSplitsMs { get; set; } = new();
    
    [JsonPropertyName("lapSplitsDisplay")]
    public List<string> LapSplitsDisplay { get; set; } = new();
    
    [JsonPropertyName("fastestLapMs")]
    public int? FastestLapMs { get; set; }
    
    [JsonPropertyName("fastestLapDisplay")]
    public string FastestLapDisplay { get; set; } = string.Empty;
    
    [JsonPropertyName("ghostFilePath")]
    public string GhostFilePath { get; set; } = string.Empty;
    
    [JsonPropertyName("dateSet")]
    public string DateSet { get; set; } = string.Empty;
    
    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
    
    [JsonPropertyName("rank")]
    public int Rank { get; set; }
}

public enum GhostCc
{
    Cc150 = 150,
    Cc200 = 200
}

public enum GhostLeaderboardType
{
    Regular,
    Flap
}