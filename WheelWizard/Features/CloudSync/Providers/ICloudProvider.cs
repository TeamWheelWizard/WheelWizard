namespace WheelWizard.CloudSync.Providers;

public sealed record RemoteFileInfo(string Path, long Size, DateTimeOffset? LastModifiedUtc, string? ETag);

public interface ICloudProvider
{
    CloudProviderType ProviderType { get; }
    Task AuthenticateAsync();

    /// <summary>Checks saved credentials without starting an interactive sign-in flow.</summary>
    Task<bool> IsAuthenticatedAsync();
    Task DisconnectAsync();
    Task<RemoteFileInfo?> GetFileInfoAsync(string path);
    Task DownloadAsync(string remotePath, string localPath);
    Task UploadAsync(string localPath, string remotePath);
    Task<bool> ExistsAsync(string path);
    Task<IReadOnlyList<string>> ListAsync(string path);
}

public interface ICloudProviderResolver
{
    ICloudProvider Resolve(CloudProviderType providerType);
}
