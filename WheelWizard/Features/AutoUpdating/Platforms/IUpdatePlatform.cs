using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.AutoUpdating.Platforms;

/// <summary>
/// Interface for platform-specific update logic.
/// </summary>
public interface IUpdatePlatform
{
    bool SupportsAutomaticUpdate { get; }

    /// <summary>
    /// Gets the asset for the current platform.
    /// </summary>
    GithubAsset? GetAssetForCurrentPlatform(GithubRelease release);

    /// <summary>
    /// Executes the update logic for the current platform.
    /// </summary>
    Task<OperationResult> ExecuteUpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
