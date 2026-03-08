using System.IO.Abstractions;
using System.IO.Compression;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using WheelWizard.MiiRendering.Configuration;
using WheelWizard.MiiRendering.Domain;

namespace WheelWizard.MiiRendering.Services;

public sealed class MiiRenderingResourceInstaller(
    IMiiRenderingAssetApi assetApi,
    IMiiRenderingResourceLocator resourceLocator,
    IFileSystem fileSystem,
    MiiRenderingConfiguration configuration,
    ILogger<MiiRenderingResourceInstaller> logger
) : IMiiRenderingResourceInstaller
{
    private const string ArchiveEntryPath = "asset/model/character/mii/AFLResHigh_2_3.dat";
    private const string TemporaryExtractedFileName = "FFLResHigh.dat.partial";
    private const int MaxDownloadAttempts = 3;

    public string ManagedResourcePath => configuration.ManagedResourcePath;

    public OperationResult<string> GetResolvedResourcePath() => resourceLocator.GetFflResourcePath();

    public async Task<OperationResult<string>> DownloadAndInstallAsync(
        IProgress<MiiRenderingInstallerProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            return await DownloadAndInstallCoreAsync(progress, cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            logger.LogInformation(exception, "Mii rendering resource download cancelled.");
            return new OperationError { Message = "Mii rendering resource download cancelled.", Exception = exception };
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to install Mii rendering resource.");
            return new OperationError { Message = "Failed to install the Mii rendering resource.", Exception = exception };
        }
    }

    private async Task<OperationResult<string>> DownloadAndInstallCoreAsync(
        IProgress<MiiRenderingInstallerProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var targetDirectory = fileSystem.Path.GetDirectoryName(configuration.ManagedResourcePath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return Fail("Unable to determine the Mii rendering resource folder.");

        fileSystem.Directory.CreateDirectory(targetDirectory);
        var temporaryExtractedPath = fileSystem.Path.Combine(targetDirectory, TemporaryExtractedFileName);

        try
        {
            var archiveBufferResult = await DownloadArchiveAsync(progress, cancellationToken);
            if (archiveBufferResult.IsFailure)
                return archiveBufferResult.Error!;

            var archiveBuffer = archiveBufferResult.Value;
            progress?.Report(new("Extracting resource", archiveBuffer.Length, archiveBuffer.Length));
            await ExtractResourceAsync(archiveBuffer, temporaryExtractedPath, cancellationToken);

            var validationResult = ValidateExtractedFile(temporaryExtractedPath);
            if (validationResult.IsFailure)
                return validationResult.Error!;

            progress?.Report(new("Finalizing install", archiveBuffer.Length, archiveBuffer.Length));
            fileSystem.File.Move(temporaryExtractedPath, configuration.ManagedResourcePath, overwrite: true);

            logger.LogInformation("Installed Mii rendering resource to {ResourcePath}", configuration.ManagedResourcePath);
            return configuration.ManagedResourcePath;
        }
        finally
        {
            TryDeleteFile(temporaryExtractedPath);
        }
    }

    private async Task<OperationResult<byte[]>> DownloadArchiveAsync(
        IProgress<MiiRenderingInstallerProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        for (var attempt = 1; attempt <= MaxDownloadAttempts; attempt++)
        {
            progress?.Report(new($"Downloading archive (attempt {attempt}/{MaxDownloadAttempts})", 0, null));

            using var response = await assetApi.DownloadArchiveAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var bufferStream = new MemoryStream(totalBytes is > 0 and <= int.MaxValue ? (int)totalBytes.Value : 0);

            var bytesDownloaded = await CopyWithProgressAsync(
                responseStream,
                bufferStream,
                totalBytes,
                progress,
                cancellationToken,
                $"Downloading archive (attempt {attempt}/{MaxDownloadAttempts})"
            );

            var archiveBytes = bufferStream.ToArray();
            var validationResult = ValidateArchiveBytes(archiveBytes, totalBytes, bytesDownloaded, attempt);
            if (validationResult.IsSuccess)
                return archiveBytes;

            logger.LogWarning(
                "Downloaded Mii rendering archive was invalid on attempt {Attempt}: {Message}",
                attempt,
                validationResult.Error?.Message
            );
        }

        return Fail("Failed to download a valid Mii rendering archive after multiple attempts.");
    }

    private async Task ExtractResourceAsync(byte[] archiveBytes, string extractedPath, CancellationToken cancellationToken)
    {
        await using var archiveStream = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry(ArchiveEntryPath) ?? throw new InvalidDataException($"Archive did not contain '{ArchiveEntryPath}'.");

        await using var entryStream = entry.Open();
        await using var outputStream = fileSystem.File.Create(extractedPath);
        await entryStream.CopyToAsync(outputStream, cancellationToken);
    }

    private OperationResult<string> ValidateExtractedFile(string extractedPath)
    {
        if (!fileSystem.File.Exists(extractedPath))
            return Fail("Downloaded archive did not produce FFLResHigh.dat.");

        var length = fileSystem.FileInfo.New(extractedPath).Length;
        if (length < configuration.MinimumExpectedSizeBytes)
        {
            return Fail($"Downloaded FFLResHigh.dat is too small ({length} bytes). The resource appears invalid or incomplete.");
        }

        return extractedPath;
    }

    private OperationResult ValidateArchiveBytes(byte[] archiveBytes, long? expectedBytes, long actualBytes, int attempt)
    {
        if (expectedBytes.HasValue && actualBytes != expectedBytes.Value)
        {
            return Fail(
                $"Downloaded archive size mismatch on attempt {attempt}: expected {expectedBytes.Value} bytes, got {actualBytes} bytes."
            );
        }

        if (archiveBytes.Length < 4)
            return Fail($"Downloaded archive was too small on attempt {attempt}.");

        if (archiveBytes[0] != (byte)'P' || archiveBytes[1] != (byte)'K')
            return Fail($"Downloaded archive did not have a ZIP signature on attempt {attempt}.");

        try
        {
            using var archive = new ZipArchive(new MemoryStream(archiveBytes, writable: false), ZipArchiveMode.Read, leaveOpen: false);
            _ = archive.GetEntry(ArchiveEntryPath) ?? throw new InvalidDataException($"Archive did not contain '{ArchiveEntryPath}'.");
            return Ok();
        }
        catch (Exception exception)
        {
            return new OperationError
            {
                Message = $"Downloaded archive was invalid on attempt {attempt}: {exception.Message}",
                Exception = exception,
            };
        }
    }

    private static async Task<long> CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<MiiRenderingInstallerProgress>? progress,
        CancellationToken cancellationToken,
        string stage
    )
    {
        var buffer = new byte[81920];
        long totalRead = 0;

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalRead += bytesRead;
            progress?.Report(new(stage, totalRead, totalBytes));
        }

        return totalRead;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (fileSystem.File.Exists(path))
                fileSystem.File.Delete(path);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to clean up temporary Mii rendering file {Path}", path);
        }
    }
}
