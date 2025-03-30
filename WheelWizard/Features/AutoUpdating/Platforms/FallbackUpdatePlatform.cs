using Avalonia.Threading;
using Semver;
using WheelWizard.Branding;
using WheelWizard.GitHub.Domain;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.AutoUpdating.Platforms;

/// <summary>
/// Fallback platform if we have no platform 
/// </summary>
public class FallbackUpdatePlatform(IBrandingSingletonService brandingService) : IUpdatePlatform
{
    // Add this because it searches multiple times, but this popup can only happen once anyways :)
    private bool _shown;

    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release)
    {
        var installedVersion = brandingService.Branding.Version;

        var latestVersion = SemVersion.Parse(release.TagName.TrimStart('v'), SemVersionStyles.Any);
        var currentVersion = SemVersion.Parse(installedVersion, SemVersionStyles.Any);
        if (currentVersion.ComparePrecedenceTo(latestVersion) >= 0) return null;
        if (_shown) return null;
        _shown = true;
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            new MessageBoxWindow()
                .SetTitleText("New Wheel Wizard version")
                .SetInfoText("There is a new Wheel Wizard version available!\n" +
                             $"Version {release.TagName.TrimStart('v')} (You are currently on {installedVersion})\n" +
                             "You can manually update it by going to the github releases at: " +
                             "https://github.com/patchzyy/WheelWizard/releases")
                .Show();
        });

        return null;
    }

    public Task<OperationResult> ExecuteUpdateAsync(string downloadUrl) => Task.FromResult(Ok());
}
