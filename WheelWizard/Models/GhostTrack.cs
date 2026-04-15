namespace WheelWizard.Models;

public class GhostTrack
{
    public int TrackId { get; set; } // Added API track ID for leaderboard calls
    public string Name { get; set; } = string.Empty;
    public string HexValue { get; set; } = string.Empty;
    public string Console { get; set; } = string.Empty;
    public GhostTrackType TrackType { get; set; }
    
    public bool IsCustomTrack => TrackType == GhostTrackType.Custom;
    public bool IsRetro => TrackType == GhostTrackType.Retro;
    public string DisplayName => IsCustomTrack ? Name : $"{Console} {Name}";
}

public enum GhostTrackType
{
    All,
    Retro,
    Custom
}

public enum GhostLocation
{
    All,
    Online,
    Local
}

public enum GhostLocationFilter
{
    All,
    Online,
    Local
}