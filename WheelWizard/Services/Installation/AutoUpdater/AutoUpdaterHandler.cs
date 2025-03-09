using Avalonia.Threading;
using Semver;
using System.Text.Json;
using WheelWizard.Helpers;
using WheelWizard.Models.Github;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Installation.AutoUpdater;

public class AutoUpdaterHandler
{
    private readonly IUpdaterPlatform _updaterPlatform;

    //todo: look into assembly versioning c# best practices 
    public virtual string CurrentVersion => AutoUpdater.CurrentVersion;

    public AutoUpdaterHandler(IUpdaterPlatform updaterPlatform)
    {
        _updaterPlatform = updaterPlatform;
    }

    public async Task CheckForUpdatesAsync()
    {
        var latestRelease = await GetLatestReleaseAsync();
        if (latestRelease?.TagName is null)
            return;
        var asset = _updaterPlatform.GetAssetForCurrentPlatform(latestRelease);
        if (asset is null)
            return;

        var latestVersion = SemVersion.Parse(latestRelease.TagName.TrimStart('v'), SemVersionStyles.Any);
        var popupExtraText = Humanizer.ReplaceDynamic(Phrases.PopupText_NewVersionWhWz, latestVersion, CurrentVersion)!;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var updateQuestion = await new YesNoWindow()
                .SetButtonText(Common.Action_Update, Common.Action_MaybeLater)
                .SetMainText(Phrases.PopupText_WhWzUpdateAvailable)
                .SetExtraText(popupExtraText)
                .AwaitAnswer();
            if (!updateQuestion)
                return;
            await _updaterPlatform.ExecuteUpdateAsync(asset.BrowserDownloadUrl);
        });
    }

    private async Task<GithubRelease?> GetLatestReleaseAsync()
    {
        var response = await HttpClientHelper.GetAsync<string>(Endpoints.WhWzReleasesUrl);
        if (!response.Succeeded || response.Content is null)
        {
            // If it failed, it can be due to many reasons. We don't want to always throw an error,
            // since most of the times its simply because the wifi is not on or something
            // It's not useful to send that error in that case so we filter those out first.
            if (response.StatusCodeGroup != 4 && response.StatusCode is not 503 and not 504)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Failed to check for updates")
                        .SetInfoText("An error occurred while checking for updates. Please try again later. " +
                                     "\nError: " + response.StatusMessage)
                        .ShowDialog();
                });
            }

            return null;
        }

        response.Content = response.Content.Trim('\0');
        var releases = JsonSerializer.Deserialize<List<GithubRelease>>(response.Content);
        if (releases is null || releases.Count == 0) return null;

        // Get the current version
        var currentVersion = SemVersion.Parse(CurrentVersion, SemVersionStyles.Any);

        // Iterate over the latest 3 releases and find the newest one that has an asset for this platform
        GithubRelease? bestMatch = null;
        SemVersion? bestVersion = null;

        foreach (var release in releases)
        {
            if (release.TagName is null) continue;
            if (release.Prerelease) continue;

            var releaseVersion = SemVersion.Parse(release.TagName.TrimStart('v'), SemVersionStyles.Any);
            if (releaseVersion.ComparePrecedenceTo(currentVersion) <= 0) continue;

            var asset = _updaterPlatform.GetAssetForCurrentPlatform(release);
            if (asset is null) continue;

            if (bestVersion is null || releaseVersion.ComparePrecedenceTo(bestVersion) > 0)
            {
                bestMatch = release;
                bestVersion = releaseVersion;
            }
        }

        return bestMatch;
    }
}
