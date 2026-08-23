using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace WheelWizard.Recomp;

/// <summary>
/// Downloads the recomp setup executable from a GitHub release asset.
/// </summary>
public interface IRecompSetupDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destinationFilePath"/>, reporting 0-100 progress.
    /// The file is written to a temporary sibling first so a cancelled or failed download never leaves a
    /// truncated setup executable in the cache.
    /// </summary>
    Task<OperationResult> DownloadAsync(
        string url,
        string destinationFilePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    );
}

/// <inheritdoc />
public sealed class RecompSetupDownloader(
    IHttpClientFactory httpClientFactory,
    IFileSystem fileSystem,
    ILogger<RecompSetupDownloader> logger
) : IRecompSetupDownloader
{
    /// <summary>
    /// The name of the configured <see cref="HttpClient"/> used for setup downloads.
    /// </summary>
    public const string HttpClientName = "RecompSetup";

    private const int BufferSize = 81920;

    // HttpClient.Timeout stops covering the body once headers arrive (ResponseHeadersRead),
    // so without this cap a stalled connection would hang the download forever.
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    public async Task<OperationResult> DownloadAsync(
        string url,
        string destinationFilePath,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        var partialFilePath = destinationFilePath + ".part";
        try
        {
            var directory = fileSystem.Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
                fileSystem.Directory.CreateDirectory(directory);

            //todo: we still need a DI downloader that can handle stuff like this
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(DownloadTimeout);
            var linkedToken = timeoutSource.Token;

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, linkedToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(linkedToken))
            await using (var destination = fileSystem.File.Create(partialFilePath))
            {
                var buffer = new byte[BufferSize];
                long copiedBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, linkedToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), linkedToken);
                    copiedBytes += read;

                    if (totalBytes is > 0)
                        progress?.Report((int)Math.Clamp(copiedBytes * 100 / totalBytes.Value, 0, 100));
                }
            }

            if (fileSystem.File.Exists(destinationFilePath))
                fileSystem.File.Delete(destinationFilePath);
            fileSystem.File.Move(partialFilePath, destinationFilePath);

            progress?.Report(100);
            return Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeletePartialFile(partialFilePath);
            throw;
        }
        catch (OperationCanceledException)
        {
            DeletePartialFile(partialFilePath);
            logger.LogError("Downloading the recomp setup from {Url} timed out", url);
            return Fail($"The download timed out after {DownloadTimeout.TotalMinutes:0} minutes.");
        }
        catch (Exception exception)
        {
            DeletePartialFile(partialFilePath);
            logger.LogError(exception, "Failed to download the recomp setup from {Url}", url);
            return Fail(exception);
        }
    }

    private void DeletePartialFile(string partialFilePath)
    {
        try
        {
            if (fileSystem.File.Exists(partialFilePath))
                fileSystem.File.Delete(partialFilePath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to clean up the partial recomp setup download");
        }
    }
}
