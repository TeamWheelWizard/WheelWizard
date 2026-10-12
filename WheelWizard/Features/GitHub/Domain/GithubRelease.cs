namespace WheelWizard.GitHub.Domain;

public class GithubRelease
{
    public required string TagName { get; set; }
    public List<GithubAsset> Assets { get; set; } = [];
    public bool Prerelease { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
