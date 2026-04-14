using System.Text.Json.Serialization;
using WheelWizard.Services;

namespace WheelWizard.GameBanana.Domain;

public class GameBananaModPreview
{
    // Properties in common with GameBananaModDetails

    [JsonPropertyName("_idRow")]
    public required int Id { get; set; }

    [JsonPropertyName("_sName")]
    public required string Name { get; set; }

    [JsonPropertyName("_sVersion")]
    public required string Version { get; set; }

    [JsonPropertyName("_aTags")]
    [JsonConverter(typeof(GameBananaTagListJsonConverter))]
    public required List<GameBananaTag> Tags { get; set; }

    [JsonPropertyName("_sProfileUrl")]
    public required string ProfileUrl { get; set; }

    [JsonPropertyName("_aPreviewMedia")]
    public required GameBananaPreviewMedia PreviewMedia { get; set; }

    [JsonPropertyName("_nLikeCount")]
    public int LikeCount { get; set; }

    [JsonPropertyName("_nViewCount")]
    public int ViewCount { get; set; }

    [JsonPropertyName("_tsDateAdded")]
    public required long DateAdded { get; set; }

    [JsonPropertyName("_tsDateModified")]
    public required long DateModified { get; set; }

    [JsonPropertyName("_aSubmitter")]
    public required GameBananaAuthor Author { get; set; }

    [JsonPropertyName("_aGame")]
    public required GameBananaGame Game { get; set; }

    // Unique properties to the Mod Details

    [JsonPropertyName("_aRootCategory")]
    public required GameBananaCategory RootCategory { get; set; }

    /// <summary>
    /// e.g. "Mod" , "Question", "Thread", "Sound"
    /// </summary>
    [JsonPropertyName("_sModelName")]
    public required string ModelName { get; set; }

    [JsonIgnore]
    public bool UsesPatches => ModStorageSystemHelper.UsesPatches(Tags);
}
