using Semver;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;

namespace WheelWizard.AutoUpdating;

public interface IAutoUpdaterSingletonService
{
    public Task CheckForUpdatesAsync();
}

public class AutoUpdaterSingletonService(
    IUpdatePlatform updatePlatform,
    IBrandingSingletonService brandingService,
    IGitHubSingletonService gitHubService,
    IUpdatePresentation presentation
) : IAutoUpdaterSingletonService
{
    private bool _manualUpdateShown;
    private string CurrentVersion => brandingService.Branding.Version;

    public async Task CheckForUpdatesAsync()
    {
        var latestRelease = await GetLatestReleaseAsync();
        if (latestRelease?.TagName is null)
            return;

        var latestVersion = latestRelease.TagName.TrimStart('v');
        if (!updatePlatform.SupportsAutomaticUpdate)
        {
            if (!_manualUpdateShown)
            {
                _manualUpdateShown = true;
                await presentation.ShowManualUpdateAsync(latestVersion, CurrentVersion);
            }
            return;
        }

        var asset = updatePlatform.GetAssetForCurrentPlatform(latestRelease);
        if (asset is null || !await presentation.ConfirmUpdateAsync(latestVersion, CurrentVersion))
            return;

        var updateResult = await presentation.RunUpdateAsync(
            (progress, cancellation) => updatePlatform.ExecuteUpdateAsync(asset.BrowserDownloadUrl, progress, cancellation)
        );
        if (updateResult.IsFailure)
            await presentation.ShowUpdateFailureAsync(updateResult.Error.Message);
    }

    private async Task<GithubRelease?> GetLatestReleaseAsync()
    {
        var releasesResult = await gitHubService.GetReleasesAsync();
        if (releasesResult.IsFailure)
        {
            await presentation.ShowCheckFailureAsync(releasesResult.Error.Message);

            return null;
        }

        if (releasesResult.Value.Count == 0)
            return null;

        // Get the current version
        var currentVersion = SemVersion.Parse(CurrentVersion, SemVersionStyles.Any);

        // Iterate over the latest 3 releases and find the newest one that has an asset for this platform
        GithubRelease? bestMatch = null;
        SemVersion? bestVersion = null;

        foreach (var release in releasesResult.Value)
        {
            if (release.TagName == null!)
                continue;

            if (release.Prerelease)
                continue;

            var releaseVersion = SemVersion.Parse(release.TagName.TrimStart('v'), SemVersionStyles.Any);
            if (releaseVersion.ComparePrecedenceTo(currentVersion) <= 0)
                continue;

            var asset = updatePlatform.GetAssetForCurrentPlatform(release);
            if (updatePlatform.SupportsAutomaticUpdate && asset is null)
                continue;

            if (bestVersion is null || releaseVersion.ComparePrecedenceTo(bestVersion) > 0)
            {
                bestMatch = release;
                bestVersion = releaseVersion;
            }
        }

        return bestMatch;
    }
}
