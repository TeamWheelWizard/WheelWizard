using System.Text.Json.Serialization;

namespace WheelWizard.Models;

public class WorldRecordsResponse
{
    [JsonPropertyName("trackId")]
    public int TrackId { get; set; }
    
    [JsonPropertyName("trackName")]
    public string TrackName { get; set; } = string.Empty;
    
    [JsonPropertyName("activeWorldRecord")]
    public GhostSubmission ActiveWorldRecord { get; set; } = new();
}

public class TrackInfo
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

public class ApiTrack
{
    public int TrackId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Console { get; set; } = string.Empty;
    public string HexValue { get; set; } = string.Empty;
    public GhostTrackType TrackType { get; set; }
    public GhostSubmission? WorldRecord { get; set; }
    
    public bool IsCustomTrack => TrackType == GhostTrackType.Custom;
    public bool IsRetroTrack => TrackType == GhostTrackType.Retro;
    
    public static ApiTrack FromWorldRecordAndTrackInfo(WorldRecordsResponse wr, TrackInfo? trackInfo)
    {
        var track = new ApiTrack
        {
            TrackId = wr.TrackId,
            Name = wr.TrackName,
            WorldRecord = wr.ActiveWorldRecord
        };
        
        if (trackInfo != null)
        {
            track.TrackType = trackInfo.Category.ToLowerInvariant() switch
            {
                "retro" => GhostTrackType.Retro,
                "custom" => GhostTrackType.Custom,
                _ => GhostTrackType.All
            };
            
            track.Console = track.TrackType switch
            {
                GhostTrackType.Retro => "Retro",
                GhostTrackType.Custom => "Custom",
                _ => "Standard"
            };
        }
        else
        {
            track.TrackType = GhostTrackType.All;
            track.Console = "Unknown";
        }
        
        return track;
    }
}