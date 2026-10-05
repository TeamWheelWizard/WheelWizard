using Semver;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;

namespace WheelWizard.AutoUpdating;

public interface IAutoUpdaterSingletonService
{
    bool IsUpdateAvailable { get; }
    event EventHandler? UpdateAvailable;
    Task CheckForUpdatesAsync();
    Task ShowAvailableUpdateAsync();
}

public class AutoUpdaterSingletonService(
    IUpdatePlatform updatePlatform,
    IBrandingSingletonService brandingService,
    IGitHubSingletonService gitHubService,
    IUpdatePresentation presentation
) : IAutoUpdaterSingletonService
{
    private bool _manualUpdateShown;
    private bool _updatePromptOpen;
    private GithubRelease? _availableRelease;
    private IReadOnlyList<GithubRelease> _availableReleases = [];
    public bool IsUpdateAvailable => _availableRelease is not null;
    public event EventHandler? UpdateAvailable;
    private string CurrentVersion => brandingService.Branding.Version;

    public async Task CheckForUpdatesAsync()
    {
        var latestRelease = await GetLatestReleaseAsync();
        if (latestRelease?.TagName is null)
            return;

        _availableRelease = latestRelease;
        UpdateAvailable?.Invoke(this, EventArgs.Empty);
        if (!updatePlatform.SupportsAutomaticUpdate && _manualUpdateShown)
            return;

        await ShowAvailableUpdateAsync();
    }

    public async Task ShowAvailableUpdateAsync()
    {
        if (_availableRelease is not { TagName: not null } latestRelease || _updatePromptOpen)
            return;

        _updatePromptOpen = true;
        try
        {
            var latestVersion = latestRelease.TagName.TrimStart('v');
            if (!updatePlatform.SupportsAutomaticUpdate)
            {
                _manualUpdateShown = true;
                await presentation.ShowManualUpdateAsync(latestVersion, CurrentVersion, _availableReleases);
                return;
            }

            var asset = updatePlatform.GetAssetForCurrentPlatform(latestRelease);
            if (asset is null || !await presentation.ConfirmUpdateAsync(latestVersion, CurrentVersion, _availableReleases))
                return;

            var updateResult = await presentation.RunUpdateAsync(
                (progress, cancellation) => updatePlatform.ExecuteUpdateAsync(asset.BrowserDownloadUrl, progress, cancellation)
            );
            if (updateResult.IsFailure)
                await presentation.ShowUpdateFailureAsync(updateResult.Error.Message);
        }
        finally
        {
            _updatePromptOpen = false;
        }
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

        // Find the newest stable release with an asset for this platform
        GithubRelease? bestMatch = null;
        SemVersion? bestVersion = null;

        foreach (var release in releasesResult.Value)
        {
            if (release.TagName == null!)
                continue;

            if (release.Prerelease || release.Draft)
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

        _availableReleases = bestVersion is null
            ? []
            : releasesResult
                .Value.Where(release => !release.Prerelease && !release.Draft && release.TagName is not null)
                .Select(release => (Release: release, Version: SemVersion.Parse(release.TagName.TrimStart('v'), SemVersionStyles.Any)))
                .Where(item => item.Version.ComparePrecedenceTo(currentVersion) > 0 && item.Version.ComparePrecedenceTo(bestVersion) <= 0)
                .OrderByDescending(item => item.Version, SemVersion.PrecedenceComparer)
                .Take(10)
                .Select(item => item.Release)
                .ToList();
        return bestMatch;
    }
}
