using WheelWizard.GitHub.Domain;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

/// <summary>
/// Picks the newest usable recomp release out of a GitHub releases listing.
/// A release is only usable when it is not a prerelease, has a parseable <c>v*</c> tag,
/// and actually carries the setup executable.
/// </summary>
public static class RecompReleaseResolver
{
    /// <summary>
    /// The owner of the recomp staging repository.
    /// </summary>
    public const string RepositoryOwner = "patchzyy";

    /// <summary>
    /// The name of the recomp staging repository.
    /// </summary>
    public const string RepositoryName = "Wiicompiled";

    /// <summary>
    /// Returns the newest usable release, or <see langword="null"/> when the listing contains none.
    /// </summary>
    public static RecompRelease? FindLatest(IEnumerable<GithubRelease>? releases, string? assetName = null)
    {
        if (releases is null)
            return null;

        RecompRelease? best = null;
        foreach (var release in releases)
        {
            if (release.Prerelease)
                continue;

            if (!RecompVersion.TryParse(release.TagName, out var version))
                continue;

            var asset = release.Assets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, assetName ?? RecompSetupCommandBuilder.SetupFileName, StringComparison.OrdinalIgnoreCase)
            );
            if (asset is null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                continue;

            if (best is null || version.ComparePrecedenceTo(best.Version) > 0)
                best = new(release.TagName, version, asset.BrowserDownloadUrl);
        }

        return best;
    }
}
