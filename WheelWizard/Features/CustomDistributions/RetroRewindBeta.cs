using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Semver;
using SharpCompress.Archives;
using SharpCompress.Readers;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;

namespace WheelWizard.CustomDistributions;

public class RetroRewindBeta : IDistribution
{
    private readonly IDownloadService downloads;
    private readonly IDistributionPrompts _prompts;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<IDistribution> _logger;
    private readonly ISettingsManager _settingsManager;
    private readonly ICustomDistributionPaths _paths;

    public RetroRewindBeta(
        IFileSystem fileSystem,
        ILogger<IDistribution> logger,
        ISettingsManager settingsManager,
        IDownloadService downloads,
        ICustomDistributionPaths paths,
        IDistributionPrompts prompts
    )
    {
        _fileSystem = fileSystem;
        this.downloads = downloads;
        _prompts = prompts;
        _logger = logger;
        _settingsManager = settingsManager;
        _paths = paths;
    }

    public string Title => "Retro Rewind Beta";
    public string FolderName => "RRBeta";
    public string XMLFolderName => "riivolution";
    public string XMLFileName => "RRBeta";

    public async Task<OperationResult> InstallAsync(DistributionOperation operation)
    {
        if (operation.CancellationToken.IsCancellationRequested)
            return Fail("Distribution installation was cancelled.");
        var tempRootPath = _paths.BetaDownloadFolderPath;
        var tempZipPath = _paths.BetaArchivePath;
        var tempExtractionPath = _fileSystem.Path.Combine(tempRootPath, "Extracted");
        OperationResult? result = null;

        try
        {
            var removeResult = await RemoveAsync(operation);
            if (removeResult.IsFailure)
                return removeResult;

            operation.Report(new(Message: "Downloading test build"));
            if (_fileSystem.Directory.Exists(tempRootPath))
                _fileSystem.Directory.Delete(tempRootPath, recursive: true);
            _fileSystem.Directory.CreateDirectory(tempRootPath);

            var download = await downloads.DownloadDistributionAsync(
                RetroRewindEndpoints.BetaArchiveUrl,
                tempZipPath,
                operation,
                useExactPath: true
            );

            if (download.IsFailure)
                return download.Error;
            var downloadedFile = download.Value;
            if (!_fileSystem.File.Exists(downloadedFile))
                return Fail("Failed to download the testing build");

            while (true)
            {
                var password = await _prompts.RequestBetaPasswordAsync();
                if (string.IsNullOrWhiteSpace(password))
                    return Fail("Password was not provided.");

                if (_fileSystem.Directory.Exists(tempExtractionPath))
                    _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
                _fileSystem.Directory.CreateDirectory(tempExtractionPath);

                operation.Report(new(Message: t("state.extracting")));
                var badPassword = false;
                var extractResult = await Task.Run(
                    () => ExtractZipFile(downloadedFile, tempExtractionPath, operation, password, out badPassword)
                );
                if (extractResult.IsSuccess)
                    break;

                if (badPassword)
                {
                    var retry = await _prompts.ConfirmPasswordRetryAsync();
                    if (retry)
                        continue;
                    return Fail("Incorrect password.");
                }

                return extractResult;
            }

            var betaFolderSource = _fileSystem.Path.Combine(tempExtractionPath, FolderName);
            var xmlFolderSource = _fileSystem.Path.Combine(tempExtractionPath, XMLFolderName);

            if (!_fileSystem.Directory.Exists(betaFolderSource))
                return Fail($"Could not find a '{FolderName}' folder inside {tempExtractionPath}");

            if (!_fileSystem.Directory.Exists(xmlFolderSource))
                return Fail($"Could not find a '{XMLFolderName}' folder inside {tempExtractionPath}");

            var moveResult = MoveExtractedFiles(tempExtractionPath);
            if (moveResult.IsFailure)
                return moveResult;

            SaveManifest(moveResult.Value);
            result = Ok();
        }
        catch (Exception ex)
        {
            result ??= Fail(ex);
            _logger.LogError(ex, ex.Message);
        }
        finally
        {
            if (_fileSystem.Directory.Exists(tempRootPath))
                _fileSystem.Directory.Delete(tempRootPath, recursive: true);
        }

        return result;
    }

    public Task<OperationResult> UpdateAsync(DistributionOperation operation) => InstallAsync(operation);

    public Task<OperationResult> RemoveAsync(DistributionOperation operation)
    {
        var rootPath = _paths.RootFolderPath;

        foreach (var entry in LoadManifest())
        {
            if (!PathSafety.TryGetPathWithinDirectory(rootPath, entry, out var fullPath))
                continue;

            if (_fileSystem.File.Exists(fullPath))
                _fileSystem.File.Delete(fullPath);
            else if (_fileSystem.Directory.Exists(fullPath))
                _fileSystem.Directory.Delete(fullPath, recursive: true);
        }

        if (_fileSystem.Directory.Exists(_paths.BetaFolderPath))
            _fileSystem.Directory.Delete(_paths.BetaFolderPath, recursive: true);
        if (_fileSystem.File.Exists(_paths.BetaXmlFilePath))
            _fileSystem.File.Delete(_paths.BetaXmlFilePath);
        if (_fileSystem.File.Exists(_paths.BetaManifestFilePath))
            _fileSystem.File.Delete(_paths.BetaManifestFilePath);

        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> ReinstallAsync(DistributionOperation operation)
    {
        var removeResult = await RemoveAsync(operation);
        if (removeResult.IsFailure)
            return removeResult;

        return await InstallAsync(operation);
    }

    public Task<OperationResult<WheelWizardStatus>> GetCurrentStatusAsync()
    {
        if (!_settingsManager.PathsSetupCorrectly())
            return Task.FromResult(Ok(WheelWizardStatus.ConfigNotFinished));

        var isInstalled = _fileSystem.Directory.Exists(_paths.BetaFolderPath) && _fileSystem.File.Exists(_paths.BetaXmlFilePath);

        return Task.FromResult(Ok(isInstalled ? WheelWizardStatus.Ready : WheelWizardStatus.NotInstalled));
    }

    public SemVersion? GetCurrentVersion() => null;

    private OperationResult ExtractZipFile(
        string zipPath,
        string destinationDirectory,
        DistributionOperation operation,
        string password,
        out bool badPassword
    )
    {
        badPassword = false;
        try
        {
            using var archiveStream = _fileSystem.File.OpenRead(zipPath);
            using var archive = ArchiveFactory.OpenArchive(archiveStream, new ReaderOptions { Password = password });
            var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
            if (entries.Count == 0)
                return Ok();

            operation.Report(new(Message: t("state.extracting"), Goal: $"Extracting {entries.Count} files"));

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (!PathSafety.TryNormalizeRelativePath(entry.Key ?? string.Empty, out var normalized))
                    continue;

                if (!TryGetRelativeExtractionPath(normalized, out var relativePath))
                    return Fail(
                        $"Unexpected file in the test archive: '{entry.Key}' (normalized: '{normalized}'). Please contact the developers."
                    );

                if (!PathSafety.TryGetPathWithinDirectory(destinationDirectory, relativePath, out var destinationPath))
                    return Fail("The file path is outside the destination directory. Please contact the developers.");

                var destinationDir = _fileSystem.Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                    _fileSystem.Directory.CreateDirectory(destinationDir);

                using var entryStream = entry.OpenEntryStream();
                using var outputStream = _fileSystem.File.Create(destinationPath);
                entryStream.CopyTo(outputStream);

                var percent = (int)(((i + 1) / (double)entries.Count) * 100);
                operation.Report(new(Percent: percent));
            }

            return Ok();
        }
        catch (Exception ex)
        {
            badPassword = IsBadPasswordException(ex);
            return badPassword ? Fail("Incorrect password.") : Fail(ex);
        }
    }

    private static bool IsBadPasswordException(Exception ex)
    {
        if (ex is CryptographicException)
            return true;

        if (!string.IsNullOrWhiteSpace(ex.Message) && ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
            return true;

        return ex.InnerException != null && IsBadPasswordException(ex.InnerException);
    }

    private bool TryGetRelativeExtractionPath(string normalizedPath, out string relativePath)
    {
        relativePath = string.Empty;
        var archivePath = normalizedPath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

        if (archivePath.Equals(FolderName, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = FolderName;
            return true;
        }

        if (archivePath.StartsWith($"{FolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = Path.Combine(FolderName, archivePath.Substring(FolderName.Length + 1).Replace('/', Path.DirectorySeparatorChar));
            return true;
        }

        if (archivePath.Equals(XMLFolderName, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = XMLFolderName;
            return true;
        }

        if (archivePath.StartsWith($"{XMLFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = Path.Combine(
                XMLFolderName,
                archivePath.Substring(XMLFolderName.Length + 1).Replace('/', Path.DirectorySeparatorChar)
            );
            return true;
        }

        return false;
    }

    private OperationResult<List<string>> MoveExtractedFiles(string tempExtractionPath)
    {
        var destinationRoot = _paths.RootFolderPath;
        _fileSystem.Directory.CreateDirectory(destinationRoot);

        var betaFolderSource = _fileSystem.Path.Combine(tempExtractionPath, FolderName);
        var xmlFolderSource = _fileSystem.Path.Combine(tempExtractionPath, XMLFolderName);

        var manifestEntries = new List<string>();
        var sourceFiles = _fileSystem
            .Directory.EnumerateFiles(betaFolderSource, "*", SearchOption.AllDirectories)
            .Concat(_fileSystem.Directory.EnumerateFiles(xmlFolderSource, "*", SearchOption.AllDirectories));

        foreach (var file in sourceFiles)
        {
            var relativePath = _fileSystem.Path.GetRelativePath(tempExtractionPath, file);
            if (IsRiivolutionPath(relativePath) && !IsBetaRiivolutionFile(relativePath))
            {
                _logger.LogWarning("Skipping non-beta riivolution file: {RelativePath}", relativePath);
                continue;
            }

            if (!PathSafety.TryGetPathWithinDirectory(destinationRoot, relativePath, out var destinationPath))
                return Fail("The file path is outside the destination directory. Please contact the developers.");

            var destinationDirectory = _fileSystem.Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDirectory))
                _fileSystem.Directory.CreateDirectory(destinationDirectory);

            _fileSystem.File.Move(file, destinationPath, overwrite: true);

            var manifestRelativePath = _fileSystem.Path.GetRelativePath(destinationRoot, destinationPath);
            manifestEntries.Add(manifestRelativePath);
        }

        return Ok(manifestEntries);
    }

    private static bool IsRiivolutionPath(string relativePath)
    {
        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        return normalized.StartsWith("riivolution/", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("riivolution", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBetaRiivolutionFile(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        return fileName.StartsWith("RRBeta", StringComparison.OrdinalIgnoreCase);
    }

    private void SaveManifest(List<string> entries)
    {
        try
        {
            var manifestDirectory = _fileSystem.Path.GetDirectoryName(_paths.BetaManifestFilePath);
            if (!string.IsNullOrEmpty(manifestDirectory))
                _fileSystem.Directory.CreateDirectory(manifestDirectory);

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            _fileSystem.File.WriteAllText(_paths.BetaManifestFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write beta manifest");
        }
    }

    private List<string> LoadManifest()
    {
        try
        {
            if (!_fileSystem.File.Exists(_paths.BetaManifestFilePath))
                return [];

            var json = _fileSystem.File.ReadAllText(_paths.BetaManifestFilePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read beta manifest");
            return [];
        }
    }
}
