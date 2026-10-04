using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.AutoUpdating.Platforms;

public sealed class FallbackUpdatePlatform : IUpdatePlatform
{
    public bool SupportsAutomaticUpdate => false;

    public GithubAsset? GetAssetForCurrentPlatform(GithubRelease release) => null;

    public Task<OperationResult> ExecuteUpdateAsync(
        string downloadUrl,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Ok());
}
