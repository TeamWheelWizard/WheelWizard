namespace WheelWizard.RrRooms;

public sealed class RwfcLeaderboardEntry
{
    public required string Pid { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FriendCode { get; set; } = string.Empty;

    public int? Vr { get; set; }
    public int? Rank { get; set; }
    public int? ActiveRank { get; set; }

    public DateTime? LastSeen { get; set; }
    public bool IsActive { get; set; }
    public bool IsSuspicious { get; set; }

    public RwfcLeaderboardVrStats? VrStats { get; set; }

    public string? MiiData { get; set; }
}
