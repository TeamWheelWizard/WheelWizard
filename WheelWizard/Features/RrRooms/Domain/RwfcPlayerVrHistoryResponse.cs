using System.Text.Json.Serialization;

namespace WheelWizard.RrRooms;

public sealed class RwfcPlayerVrHistoryResponse
{
    [JsonPropertyName("playerId")]
    public string PlayerId { get; set; } = string.Empty;

    [JsonPropertyName("fromDate")]
    public DateTime FromDate { get; set; }

    [JsonPropertyName("toDate")]
    public DateTime ToDate { get; set; }

    [JsonPropertyName("history")]
    public List<RwfcPlayerVrHistoryEntry> History { get; set; } = [];

    [JsonPropertyName("totalVRChange")]
    public int TotalVrChange { get; set; }

    [JsonPropertyName("startingVR")]
    public int StartingVr { get; set; }

    [JsonPropertyName("endingVR")]
    public int EndingVr { get; set; }
}

public sealed class RwfcPlayerVrHistoryEntry
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("vrChange")]
    public int VrChange { get; set; }

    [JsonPropertyName("totalVR")]
    public int TotalVr { get; set; }
}
