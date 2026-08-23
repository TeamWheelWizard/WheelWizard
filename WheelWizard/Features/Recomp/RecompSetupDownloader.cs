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

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destination = fileSystem.File.Create(partialFilePath))
            {
                var buffer = new byte[BufferSize];
                long copiedBytes = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
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
        catch (OperationCanceledException)
        {
            DeletePartialFile(partialFilePath);
            throw;
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
