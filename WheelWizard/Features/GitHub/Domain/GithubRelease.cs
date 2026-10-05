namespace WheelWizard.GitHub.Domain;

public class GithubRelease
{
    public required string TagName { get; set; }
    public List<GithubAsset> Assets { get; set; } = [];
    public string? BodyHtml { get; set; }
    public bool Draft { get; set; }
    public bool Prerelease { get; set; }
}
