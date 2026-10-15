using System.Text.Json.Serialization;

namespace WheelWizard.GameBanana.Domain;

public sealed class GameBananaModType
{
    [JsonPropertyName("_idRow")]
    public int Id { get; set; }

    [JsonPropertyName("_sName")]
    public required string Name { get; set; }

    [JsonPropertyName("_sUrl")]
    public required string Url { get; set; }

    [JsonPropertyName("_bIsObsolete")]
    public bool IsObsolete { get; set; }

    public override string ToString() => Name;
}
