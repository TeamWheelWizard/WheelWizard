using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.Shared.IO;

namespace WheelWizard.Shared.Downloads;

public sealed class DownloadService(
    IHttpClientFactory clients,
    IFileSystem files,
    TimeProvider time,
    DownloadOptions options,
    ILogger<DownloadService> logger
) : IDownloadService
{
    public const string ClientName = "Downloads";

    public async Task<OperationResult<string>> DownloadAsync(
        string url,
        string destinationPath,
        bool useExactPath = false,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (options.MaxAttempts < 1)
            throw new InvalidOperationException("At least one download attempt is required.");

        string? temporaryPath = null;
        try
        {
            var destination = files.Path.GetFullPath(destinationPath);
            var directory = files.Path.GetDirectoryName(destination)!;
            files.Directory.CreateDirectory(directory);
            temporaryPath = files.Path.Combine(directory, $".download-{Guid.NewGuid():N}.tmp");

            for (var attempt = 1; attempt <= options.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var client = clients.CreateClient(ClientName);
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    var resolvedPath = useExactPath ? destination : ResolveDestination(response, url, directory);
                    var totalBytes = response.Content.Headers.ContentLength;
                    progress?.Report(new(0, totalBytes, attempt));

                    await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                    await using (var target = files.FileStream.New(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        var buffer = new byte[8192];
                        long received = 0;
                        int count;
                        while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
                        {
                            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                            received += count;
                            progress?.Report(new(received, totalBytes, attempt));
                        }
                        await target.FlushAsync(cancellationToken);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    files.File.Move(temporaryPath, resolvedPath, overwrite: true);
                    return resolvedPath;
                }
                catch (Exception exception)
                    when (attempt < options.MaxAttempts
                        && !cancellationToken.IsCancellationRequested
                        && exception is HttpRequestException or OperationCanceledException
                    )
                {
                    logger.LogWarning(exception, "Download attempt {Attempt} failed", attempt);
                    progress?.Report(new(0, null, attempt + 1, Retrying: true));
                    await Task.Delay(options.BaseRetryDelay * Math.Pow(2, attempt), time, cancellationToken);
                }
            }

            throw new InvalidOperationException("Download attempts ended without a result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Download failed");
            return new OperationError { Message = $"Download failed: {exception.Message}", Exception = exception };
        }
        finally
        {
            if (temporaryPath != null)
            {
                try
                {
                    if (files.File.Exists(temporaryPath))
                        files.File.Delete(temporaryPath);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to remove incomplete download {Path}", temporaryPath);
                }
            }
        }
    }

    private string ResolveDestination(HttpResponseMessage response, string url, string directory)
    {
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName ?? files.Path.GetFileName(new Uri(url).AbsolutePath);
        if (!PathSafety.TryGetSafeFileName(name, out name))
            throw new InvalidDataException("The server returned an invalid download filename.");

        var finalExtension = files.Path.GetExtension(response.RequestMessage?.RequestUri?.AbsolutePath ?? new Uri(url).AbsolutePath);
        if (!string.IsNullOrWhiteSpace(finalExtension))
            name = files.Path.ChangeExtension(name, finalExtension);
        if (!files.Path.HasExtension(name))
            name += files.Path.GetExtension(new Uri(url).AbsolutePath);

        if (!PathSafety.TryGetPathWithinDirectory(directory, name, out var result))
            throw new InvalidDataException("The download path escaped the target directory.");
        return result;
    }
}
