using System.Text.Json.Serialization;
using WheelWizard.RrRooms;

namespace WheelWizard.Models;

public class PlayerProfileResponse
{
    [JsonPropertyName("pid")]
    public string Pid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("friendCode")]
    public string FriendCode { get; set; } = string.Empty;

    [JsonPropertyName("vr")]
    public int Vr { get; set; }

    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("lastSeen")]
    public DateTime LastSeen { get; set; }

    [JsonPropertyName("isSuspicious")]
    public bool IsSuspicious { get; set; }

    [JsonPropertyName("vrStats")]
    public RwfcLeaderboardVrStats? VrStats { get; set; }

    [JsonPropertyName("miiData")]
    public string MiiData { get; set; } = string.Empty;
}
