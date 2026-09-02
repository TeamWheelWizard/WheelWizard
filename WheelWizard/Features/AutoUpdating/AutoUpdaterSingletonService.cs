using Avalonia.Threading;
using Semver;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Helpers;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.AutoUpdating;

public interface IAutoUpdaterSingletonService
{
    public Task CheckForUpdatesAsync(bool updateAutomatically = false);
}

public class AutoUpdaterSingletonService(
    IUpdatePlatform updatePlatform,
    IBrandingSingletonService brandingService,
    IGitHubSingletonService gitHubService
) : IAutoUpdaterSingletonService
{
    private string CurrentVersion => brandingService.Branding.Version;

    public async Task CheckForUpdatesAsync(bool updateAutomatically = false)
    {
        // TODO: How to run this in a background thread?
        var latestRelease = await GetLatestReleaseAsync(showErrors: !updateAutomatically);
        if (latestRelease?.TagName is null)
            return;

        var asset = updatePlatform.GetAssetForCurrentPlatform(latestRelease);
        if (asset is null)
            return;

        var latestVersion = SemVersion.Parse(latestRelease.TagName.TrimStart('v'), SemVersionStyles.Any);
        var popupExtraText = t("question.new_version_wh_wz.extra", latestVersion, CurrentVersion)!;

        var shouldUpdate = updateAutomatically;
        if (!updateAutomatically)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                shouldUpdate = await new YesNoWindow()
                    .SetButtonText(t("action.update"), t("action.maybe_later"))
                    .SetMainText(t("question.new_version_wh_wz.title"))
                    .SetExtraText(popupExtraText)
                    .AwaitAnswer();
            });
        }

        if (!shouldUpdate)
            return;

        var updateResult = await updatePlatform.ExecuteUpdateAsync(asset.BrowserDownloadUrl, restartApplication: !updateAutomatically);

        if (updateResult.IsFailure && !updateAutomatically)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText(updateResult.Error.Message)
                .ShowDialog();
        }
    }

    private async Task<GithubRelease?> GetLatestReleaseAsync(bool showErrors)
    {
        var releasesResult = await gitHubService.GetReleasesAsync();
        if (releasesResult.IsFailure)
        {
            if (showErrors)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Failed to check for updates")
                        .SetInfoText(
                            "An error occurred while checking for updates. Please try again later. "
                                + "\nError: "
                                + releasesResult.Error.Message
                        )
                        .ShowDialog();
                });
            }

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
            if (asset is null)
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
