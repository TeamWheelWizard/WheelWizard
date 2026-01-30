using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Semver;
using SharpCompress.Archives;
using SharpCompress.Readers;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.CustomDistributions;

public class RetroRewindBeta : IDistribution
{
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<IDistribution> _logger;

    public RetroRewindBeta(IFileSystem fileSystem, ILogger<IDistribution> logger)
    {
        _fileSystem = fileSystem;
        _logger = logger;
    }

    public string Title => "Retro Rewind Beta";
    public string FolderName => "RRBeta";
    public string XMLFolderName => "riivolution";
    public string XMLFileName => "RRBeta";

    public async Task<OperationResult> InstallAsync(ProgressWindow progressWindow)
    {
        var tempRootPath = PathManager.RrBetaTempFolderPath;
        var tempZipPath = PathManager.RrBetaTempFilePath;
        var tempExtractionPath = _fileSystem.Path.Combine(tempRootPath, "Extracted");
        OperationResult? result = null;

        try
        {
            var removeResult = await RemoveAsync(progressWindow);
            if (removeResult.IsFailure)
                return removeResult;

            progressWindow.SetExtraText("Downloading test build");
            if (_fileSystem.Directory.Exists(tempRootPath))
                _fileSystem.Directory.Delete(tempRootPath, recursive: true);
            _fileSystem.Directory.CreateDirectory(tempRootPath);

            var downloadedFile = await DownloadHelper.DownloadToLocationAsync(
                Endpoints.RRTestersZipUrl,
                tempZipPath,
                progressWindow,
                ForceGivenFilePath: true
            );

            if (string.IsNullOrWhiteSpace(downloadedFile) || !_fileSystem.File.Exists(downloadedFile))
                return Fail("Failed to download the testing build");

            while (true)
            {
                var password = await RequestPasswordAsync();
                if (string.IsNullOrWhiteSpace(password))
                    return Fail("Password was not provided.");

                if (_fileSystem.Directory.Exists(tempExtractionPath))
                    _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
                _fileSystem.Directory.CreateDirectory(tempExtractionPath);

                progressWindow.SetExtraText(Common.State_Extracting);
                var badPassword = false;
                var extractResult = await Task.Run(
                    () => ExtractZipFile(downloadedFile, tempExtractionPath, progressWindow, password, out badPassword)
                );
                if (extractResult.IsSuccess)
                    break;

                if (badPassword)
                {
                    var retry = await new YesNoWindow()
                        .SetMainText("Incorrect password")
                        .SetExtraText("Do you want to try again?")
                        .SetButtonText("Retry", "Cancel")
                        .AwaitAnswer();
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

    public Task<OperationResult> UpdateAsync(ProgressWindow progressWindow) => InstallAsync(progressWindow);

    public Task<OperationResult> RemoveAsync(ProgressWindow progressWindow)
    {
        var rootPath = PathManager.RiivolutionWhWzFolderPath;
        var rootFullPath = _fileSystem.Path.GetFullPath(rootPath + Path.AltDirectorySeparatorChar);

        foreach (var entry in LoadManifest())
        {
            var fullPath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(rootPath, entry));
            if (!fullPath.StartsWith(rootFullPath, StringComparison.Ordinal))
                continue;

            if (_fileSystem.File.Exists(fullPath))
                _fileSystem.File.Delete(fullPath);
            else if (_fileSystem.Directory.Exists(fullPath))
                _fileSystem.Directory.Delete(fullPath, recursive: true);
        }

        if (_fileSystem.Directory.Exists(PathManager.RrBetaFolderPath))
            _fileSystem.Directory.Delete(PathManager.RrBetaFolderPath, recursive: true);
        if (_fileSystem.File.Exists(PathManager.RrBetaXmlFilePath))
            _fileSystem.File.Delete(PathManager.RrBetaXmlFilePath);
        if (_fileSystem.File.Exists(PathManager.RrBetaManifestFilePath))
            _fileSystem.File.Delete(PathManager.RrBetaManifestFilePath);

        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> ReinstallAsync(ProgressWindow progressWindow)
    {
        var removeResult = await RemoveAsync(progressWindow);
        if (removeResult.IsFailure)
            return removeResult;

        return await InstallAsync(progressWindow);
    }

    public Task<OperationResult<WheelWizardStatus>> GetCurrentStatusAsync()
    {
        if (!SettingsHelper.PathsSetupCorrectly())
            return Task.FromResult(Ok(WheelWizardStatus.ConfigNotFinished));

        var isInstalled =
            _fileSystem.Directory.Exists(PathManager.RrBetaFolderPath) && _fileSystem.File.Exists(PathManager.RrBetaXmlFilePath);

        return Task.FromResult(Ok(isInstalled ? WheelWizardStatus.Ready : WheelWizardStatus.NotInstalled));
    }

    public SemVersion? GetCurrentVersion() => null;

    private async Task<string?> RequestPasswordAsync()
    {
        return await new TextInputWindow()
            .SetMainText("Please enter Password")
            .SetPlaceholderText("Password")
            .SetButtonText("Cancel", "Submit")
            .ShowDialog();
    }

    private OperationResult ExtractZipFile(
        string zipPath,
        string destinationDirectory,
        ProgressWindow progressWindow,
        string password,
        out bool badPassword
    )
    {
        badPassword = false;
        try
        {
            using var archive = ArchiveFactory.Open(zipPath, new ReaderOptions { Password = password });
            var entries = archive.Entries.Where(entry => !entry.IsDirectory).ToList();
            if (entries.Count == 0)
                return Ok();

            Dispatcher.UIThread.Post(() =>
            {
                progressWindow.SetExtraText(Common.State_Extracting).SetGoal($"Extracting {entries.Count} files");
            });

            var absoluteDestinationPath = _fileSystem.Path.GetFullPath(destinationDirectory + Path.AltDirectorySeparatorChar);

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var normalized = NormalizeEntryPath(entry.Key ?? string.Empty);
                if (string.IsNullOrWhiteSpace(normalized))
                    continue;

                if (!TryGetRelativeExtractionPath(normalized, out var relativePath))
                    return Fail("Unexpected file in the test archive. Please contact the developers.");

                var destinationPath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(destinationDirectory, relativePath));
                if (!destinationPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal))
                    return Fail("The file path is outside the destination directory. Please contact the developers.");

                var destinationDir = _fileSystem.Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                    _fileSystem.Directory.CreateDirectory(destinationDir);

                using var entryStream = entry.OpenEntryStream();
                using var outputStream = File.Create(destinationPath);
                entryStream.CopyTo(outputStream);

                var percent = (int)(((i + 1) / (double)entries.Count) * 100);
                Dispatcher.UIThread.Post(() =>
                {
                    progressWindow.UpdateProgress(percent);
                });
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

    private static string NormalizeEntryPath(string path) => path.Replace('\\', '/').TrimStart('/');

    private bool TryGetRelativeExtractionPath(string normalizedPath, out string relativePath)
    {
        relativePath = string.Empty;

        if (normalizedPath.Equals(FolderName, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = FolderName;
            return true;
        }

        if (normalizedPath.StartsWith($"{FolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = Path.Combine(
                FolderName,
                normalizedPath.Substring(FolderName.Length + 1).Replace('/', Path.DirectorySeparatorChar)
            );
            return true;
        }

        if (normalizedPath.Equals(XMLFolderName, StringComparison.OrdinalIgnoreCase))
        {
            relativePath = XMLFolderName;
            return true;
        }

        if (normalizedPath.StartsWith($"{XMLFolderName}/", StringComparison.OrdinalIgnoreCase))
        {
            relativePath = Path.Combine(
                XMLFolderName,
                normalizedPath.Substring(XMLFolderName.Length + 1).Replace('/', Path.DirectorySeparatorChar)
            );
            return true;
        }

        return false;
    }

    private OperationResult<List<string>> MoveExtractedFiles(string tempExtractionPath)
    {
        var destinationRoot = PathManager.RiivolutionWhWzFolderPath;
        _fileSystem.Directory.CreateDirectory(destinationRoot);

        var betaFolderSource = _fileSystem.Path.Combine(tempExtractionPath, FolderName);
        var xmlFolderSource = _fileSystem.Path.Combine(tempExtractionPath, XMLFolderName);

        var manifestEntries = new List<string>();
        var sourceFiles = _fileSystem
            .Directory.EnumerateFiles(betaFolderSource, "*", SearchOption.AllDirectories)
            .Concat(_fileSystem.Directory.EnumerateFiles(xmlFolderSource, "*", SearchOption.AllDirectories));

        var absoluteDestinationRoot = _fileSystem.Path.GetFullPath(destinationRoot + Path.AltDirectorySeparatorChar);

        foreach (var file in sourceFiles)
        {
            var relativePath = _fileSystem.Path.GetRelativePath(tempExtractionPath, file);
            if (IsRiivolutionPath(relativePath) && !IsBetaRiivolutionFile(relativePath))
            {
                _logger.LogWarning("Skipping non-beta riivolution file: {RelativePath}", relativePath);
                continue;
            }

            var destinationPath = _fileSystem.Path.Combine(destinationRoot, relativePath);
            var fullDestinationPath = _fileSystem.Path.GetFullPath(destinationPath);
            if (!fullDestinationPath.StartsWith(absoluteDestinationRoot, StringComparison.Ordinal))
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
            var manifestDirectory = _fileSystem.Path.GetDirectoryName(PathManager.RrBetaManifestFilePath);
            if (!string.IsNullOrEmpty(manifestDirectory))
                _fileSystem.Directory.CreateDirectory(manifestDirectory);

            var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
            _fileSystem.File.WriteAllText(PathManager.RrBetaManifestFilePath, json);
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
            if (!_fileSystem.File.Exists(PathManager.RrBetaManifestFilePath))
                return [];

            var json = _fileSystem.File.ReadAllText(PathManager.RrBetaManifestFilePath);
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read beta manifest");
            return [];
        }
    }
}
