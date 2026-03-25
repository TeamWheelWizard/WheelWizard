namespace WheelWizard.MiiRendering.Services;

public readonly record struct MiiRenderingInstallerProgress(string Stage, long BytesDownloaded, long? TotalBytes);

public interface IMiiRenderingResourceInstaller
{
    string ManagedResourcePath { get; }

    OperationResult<string> GetResolvedResourcePath();

    Task<OperationResult<string>> DownloadAndInstallAsync(
        IProgress<MiiRenderingInstallerProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
