using WheelWizard.GitHub.Domain;
using WheelWizard.Shared.Downloads;

namespace WheelWizard.AutoUpdating;

public interface IUpdatePresentation
{
    Task<bool> ConfirmUpdateAsync(string latestVersion, string currentVersion, IReadOnlyList<GithubRelease> releases);
    Task<bool> ConfirmElevationAsync();
    Task ShowCheckFailureAsync(string message);
    Task ShowUpdateFailureAsync(string message);
    Task ShowManualUpdateAsync(string latestVersion, string currentVersion, IReadOnlyList<GithubRelease> releases);
    Task<OperationResult> RunUpdateAsync(Func<IProgress<DownloadProgress>, CancellationToken, Task<OperationResult>> operation);
}
