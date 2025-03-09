using WheelWizard.Models.Github;

namespace WheelWizard.Services.Installation.AutoUpdater;

public interface IUpdaterPlatform
{
    GithubAsset? GetAssetForCurrentPlatform(GithubRelease release);
    Task ExecuteUpdateAsync(string downloadUrl);
}
