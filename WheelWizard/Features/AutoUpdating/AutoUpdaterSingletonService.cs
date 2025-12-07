using Avalonia.Threading;
using Semver;
using WheelWizard.AutoUpdating.Platforms;
using WheelWizard.Branding;
using WheelWizard.GitHub;
using WheelWizard.GitHub.Domain;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.AutoUpdating;

public interface IAutoUpdaterSingletonService
{
    public Task CheckForUpdatesAsync();
}

public class AutoUpdaterSingletonService(
    IUpdatePlatform updatePlatform,
    IBrandingSingletonService brandingService,
    IGitHubSingletonService gitHubService
) : IAutoUpdaterSingletonService
{
    private string CurrentVersion => brandingService.Branding.Version;

    public async Task CheckForUpdatesAsync()
    {
        // TODO: How to run this in a background thread?
        var latestRelease = await GetLatestReleaseAsync();
        if (latestRelease?.TagName is null)
            return;

        var asset = updatePlatform.GetAssetForCurrentPlatform(latestRelease);
        if (asset is null)
            return;

        var latestVersion = SemVersion.Parse(latestRelease.TagName.TrimStart('v'), SemVersionStyles.Any);
        var popupExtraText = Humanizer.ReplaceDynamic(Phrases.Question_NewVersionWhWz_Extra, latestVersion, CurrentVersion)!;

        var shouldUpdate = false;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            shouldUpdate = await new YesNoWindow()
                .SetButtonText(Common.Action_Update, Common.Action_MaybeLater)
                .SetMainText(Phrases.Question_NewVersionWhWz_Title)
                .SetExtraText(popupExtraText)
                .AwaitAnswer();
        });

        if (!shouldUpdate)
            return;

        var updateResult = await updatePlatform.ExecuteUpdateAsync(asset.BrowserDownloadUrl);

        if (updateResult.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Unable to update Wheel Wizard")
                .SetInfoText(updateResult.Error.Message)
                .ShowDialog();
        }
    }

    private async Task<GithubRelease?> GetLatestReleaseAsync()
    {
        var releasesResult = await gitHubService.GetReleasesAsync();
        if (releasesResult.IsFailure)
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
