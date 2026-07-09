using System.Text.Json.Serialization;

namespace WheelWizard.GameBanana.Domain;

public class GameBananaModDetails
{
    // Properties in common with GameBananaModPreview

    [JsonPropertyName("_idRow")]
    public required int Id { get; set; }

    [JsonPropertyName("_sName")]
    public required string Name { get; set; }

    [JsonPropertyName("_sVersion")]
    public required string Version { get; set; }

    [JsonPropertyName("_sProfileUrl")]
    public required string ProfileUrl { get; set; }

    [JsonPropertyName("_aPreviewMedia")]
    public GameBananaPreviewMedia? PreviewMedia { get; set; }

    [JsonPropertyName("_nLikeCount")]
    public required int LikeCount { get; set; }

    [JsonPropertyName("_nViewCount")]
    public required int ViewCount { get; set; }

    [JsonPropertyName("_tsDateAdded")]
    public required long DateAdded { get; set; }

    [JsonPropertyName("_tsDateModified")]
    public required long DateModified { get; set; }

    [JsonPropertyName("_bIsObsolete")]
    public required bool IsObsolete { get; set; }

    [JsonPropertyName("_aSubmitter")]
    public required GameBananaAuthor Author { get; set; }

    [JsonPropertyName("_aGame")]
    public required GameBananaGame Game { get; set; }

    // Unique properties to the Mod Details

    [JsonPropertyName("_aCategory")]
    public required GameBananaCategory Category { get; set; }

    [JsonPropertyName("_aSuperCategory")]
    public GameBananaCategory? SuperCategory { get; set; }

    [JsonPropertyName("_sText")]
    public required string Text { get; set; }

    [JsonPropertyName("_sLicense")]
    public required string License { get; set; }

    [JsonPropertyName("_aLicenseCheckList")]
    public GameBananaLicenseAllowance? LicenseAllowance { get; set; }

    [JsonPropertyName("_nDownloadCount")]
    public required int DownloadCount { get; set; }

    [JsonPropertyName("_aFiles")]
    public List<GameBananaModFiles>? Files { get; set; }

    [JsonPropertyName("_aArchivedFiles")]
    public List<GameBananaModFiles>? ArchivedFiles { get; set; }
}
