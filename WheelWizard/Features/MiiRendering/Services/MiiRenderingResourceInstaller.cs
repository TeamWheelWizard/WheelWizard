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
    private const string TemporaryArchiveFileName = "ffl-resource-download.zip";
    private const string TemporaryExtractedFileName = "FFLResHigh.dat.partial";

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

        var temporaryArchivePath = fileSystem.Path.Combine(targetDirectory, TemporaryArchiveFileName);
        var temporaryExtractedPath = fileSystem.Path.Combine(targetDirectory, TemporaryExtractedFileName);

        try
        {
            progress?.Report(new("Downloading archive", 0, null));

            using var response = await assetApi.DownloadArchiveAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            await using (var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var archiveFileStream = fileSystem.File.Create(temporaryArchivePath))
            {
                await CopyWithProgressAsync(responseStream, archiveFileStream, totalBytes, progress, cancellationToken);
            }

            progress?.Report(new("Extracting resource", totalBytes ?? 0, totalBytes));
            await ExtractResourceAsync(temporaryArchivePath, temporaryExtractedPath, cancellationToken);

            var validationResult = ValidateExtractedFile(temporaryExtractedPath);
            if (validationResult.IsFailure)
                return validationResult.Error!;

            progress?.Report(new("Finalizing install", totalBytes ?? 0, totalBytes));
            fileSystem.File.Move(temporaryExtractedPath, configuration.ManagedResourcePath, overwrite: true);

            logger.LogInformation("Installed Mii rendering resource to {ResourcePath}", configuration.ManagedResourcePath);
            return configuration.ManagedResourcePath;
        }
        finally
        {
            TryDeleteFile(temporaryArchivePath);
            TryDeleteFile(temporaryExtractedPath);
        }
    }

    private async Task ExtractResourceAsync(string archivePath, string extractedPath, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
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

    private static async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        IProgress<MiiRenderingInstallerProgress>? progress,
        CancellationToken cancellationToken
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
            progress?.Report(new("Downloading archive", totalRead, totalBytes));
        }
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
