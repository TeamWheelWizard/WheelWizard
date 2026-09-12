using WheelWizard.Shared.Downloads;

namespace WheelWizard.CustomDistributions;

public sealed record DistributionProgress(
    string? Message = null,
    int? Percent = null,
    string? Goal = null,
    long? TotalBytes = null,
    bool? CanCancel = null
);

public sealed record DistributionOperation(IProgress<DistributionProgress>? Progress = null, CancellationToken CancellationToken = default)
{
    public void Report(DistributionProgress progress) => Progress?.Report(progress);
}

public static class DistributionDownloads
{
    public static async Task<OperationResult<string>> DownloadDistributionAsync(
        this IDownloadService downloads,
        string url,
        string destination,
        DistributionOperation operation,
        bool useExactPath = false
    )
    {
        operation.Report(new(CanCancel: true));
        try
        {
            return await downloads.DownloadAsync(
                url,
                destination,
                useExactPath,
                new DownloadReporter(operation),
                operation.CancellationToken
            );
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            return Fail("Distribution download was cancelled.");
        }
        finally
        {
            operation.Report(new(CanCancel: false));
        }
    }

    private sealed class DownloadReporter(DistributionOperation operation) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) =>
            operation.Report(
                value.Retrying
                    ? new(Message: $"Retrying... Attempt {value.Attempt}")
                    : new(Percent: value.Percentage, TotalBytes: value.TotalBytes)
            );
    }
}
