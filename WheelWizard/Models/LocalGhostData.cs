using System.Linq;

namespace WheelWizard.Models;

/// <summary>
/// Represents a local ghost record parsed from an RKG file
/// </summary>
public class LocalGhostData
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName => Path.GetFileNameWithoutExtension(FilePath);
    
    // Track information
    public uint TrackId { get; set; }
    public string TrackName { get; set; } = string.Empty;
    
    // Character/Vehicle setup
    public byte CharacterId { get; set; }
    public string CharacterName { get; set; } = string.Empty;
    public byte VehicleId { get; set; }
    public string VehicleName { get; set; } = string.Empty;
    public byte ControllerType { get; set; }
    public string ControllerName { get; set; } = string.Empty;
    
    // Timing data
    public uint TotalTimeMs { get; set; }
    public string TotalTimeDisplay => FormatTime(TotalTimeMs);
    public List<uint> LapSplitsMs { get; set; } = new();
    public List<string> LapSplitsDisplay => LapSplitsMs.Select(FormatTime).ToList();
    public uint FastestLapMs => LapSplitsMs.Count > 0 ? LapSplitsMs.Min() : 0;
    public string FastestLapDisplay => FormatTime(FastestLapMs);
    public double AverageLapMs => LapSplitsMs.Count > 0 ? LapSplitsMs.Select(x => (double)x).Average() : 0;
    public string AverageLapDisplay => FormatTime((uint)AverageLapMs);
    
    // Metadata
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("MM/dd/yyyy");
    public ushort Country { get; set; }
    public string CountryName { get; set; } = string.Empty;
    public string MiiData { get; set; } = string.Empty;
    
    // CC information (derived from folder structure)
    public int Cc { get; set; } = 150; // 150 or 200
    public bool IsVariant { get; set; } // true if in variant subfolder
    
    // Ranking information (set when loading ghosts)
    public int Rank { get; set; } = 0;
    
    private static string FormatTime(uint timeMs)
    {
        if (timeMs == 0) return "0:00.000";
        
        var totalSeconds = timeMs / 1000.0;
        var minutes = (int)(totalSeconds / 60);
        var seconds = totalSeconds % 60;
        
        return $"{minutes}:{seconds:00.000}";
    }
}

/// <summary>
/// Represents a group of local ghost records for a specific track
/// </summary>
public class LocalTrackGhosts
{
    public uint TrackId { get; set; }
    public string TrackName { get; set; } = string.Empty;
    public string HexFolderName { get; set; } = string.Empty;
    public List<LocalGhostData> Ghosts150 { get; set; } = new();
    public List<LocalGhostData> Ghosts200 { get; set; } = new();
    public List<LocalGhostData> VariantGhosts150 { get; set; } = new(); // For variant tracks
    public List<LocalGhostData> VariantGhosts200 { get; set; } = new();
    
    public int TotalGhostCount => Ghosts150.Count + Ghosts200.Count + VariantGhosts150.Count + VariantGhosts200.Count;
    public bool HasGhosts => TotalGhostCount > 0;
    
    public LocalGhostData? BestTime150 => Ghosts150.Concat(VariantGhosts150)
        .OrderBy(g => g.TotalTimeMs)
        .FirstOrDefault();
        
    public LocalGhostData? BestTime200 => Ghosts200.Concat(VariantGhosts200)
        .OrderBy(g => g.TotalTimeMs)
        .FirstOrDefault();
}