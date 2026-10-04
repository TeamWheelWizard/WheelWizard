namespace WheelWizard.Shared.Downloads;

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, int Attempt = 1, bool Retrying = false)
{
    public int Percentage => TotalBytes is > 0 ? (int)Math.Clamp(BytesReceived * 100d / TotalBytes.Value, 0, 100) : 0;
}

public sealed record DownloadOptions(int MaxAttempts = 5, TimeSpan? RetryDelay = null)
{
    public TimeSpan BaseRetryDelay => RetryDelay ?? TimeSpan.FromSeconds(1);
}

public interface IDownloadService
{
    Task<OperationResult<string>> DownloadAsync(
        string url,
        string destinationPath,
        bool useExactPath = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}
