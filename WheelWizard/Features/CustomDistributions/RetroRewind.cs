using System.IO.Abstractions;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Semver;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Services;

namespace WheelWizard.CustomDistributions;

public class RetroRewind : IDistribution
{
    private readonly IDownloadService downloads;
    private readonly IDistributionPrompts _prompts;
    private readonly IFileSystem _fileSystem;
    private readonly IApiCaller<IRetroRewindApi> _api;
    private readonly ILogger<IDistribution> _logger;
    private readonly ISettingsManager _settingsManager;
    private readonly ICustomDistributionPaths _paths;
    private readonly IDolphinPaths _dolphinPaths;

    public RetroRewind(
        IFileSystem fileSystem,
        IApiCaller<IRetroRewindApi> api,
        ILogger<IDistribution> logger,
        ISettingsManager settingsManager,
        IDownloadService downloads,
        ICustomDistributionPaths paths,
        IDolphinPaths dolphinPaths,
        IDistributionPrompts prompts
    )
    {
        _api = api;
        this.downloads = downloads;
        _prompts = prompts;
        _fileSystem = fileSystem;
        _logger = logger;
        _settingsManager = settingsManager;
        _paths = paths;
        _dolphinPaths = dolphinPaths;
    }

    public string Title => "Retro Rewind";

    // Keep in mind, whenever we download update files from the server, they are actually 1 folder higher, so it contains this folder.
    public string FolderName => "RetroRewind6";
    public string XMLFolderName => "riivolution";
    public string XMLFileName => "RetroRewind6";

    public async Task<OperationResult> InstallAsync(DistributionOperation operation)
    {
        if (GetCurrentVersion() is not null)
        {
            var removeResult = await RemoveAsync(operation);
            if (removeResult.IsFailure)
                return removeResult;
        }

        if (HasOldRksys())
        {
            if (await _prompts.ConfirmOldSaveBackupAsync())
                await BackupOldrksys();
        }
        var serverResponse = await _api.CallApiAsync(api => api.Ping()); // actual response doesnt matter
        if (serverResponse.IsFailure)
            return Fail("Could not connect to the server");

        var downloadResult = await DownloadAndExtractRetroRewind(operation);
        if (downloadResult.IsFailure)
            return downloadResult;

        if (operation.CancellationToken.IsCancellationRequested)
            return Ok();

        var updateResult = await UpdateAsync(operation);
        if (updateResult.IsFailure)
            return updateResult;

        return Ok();
    }

    private async Task<OperationResult> DownloadAndExtractRetroRewind(DistributionOperation operation)
    {
        operation.Report(new(Message: t("progress.installing_rr_first_time")));
        var downloadedZipPath = _paths.RetroRewindArchivePath;
        // where we'll do the extraction
        var tempExtractionPath = _paths.DownloadFolderPath;

        //where all distributions are stored
        var destinationParentDir = _fileSystem.DirectoryInfo.New(_paths.RootFolderPath);

        OperationResult? result = null;
        try
        {
            // 1) Download
            if (_fileSystem.Directory.Exists(tempExtractionPath))
                _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
            _fileSystem.Directory.CreateDirectory(tempExtractionPath);

            var installUrlResult = await _api.CallApiAsync(api => api.GetInstallUrl());
            if (installUrlResult.IsFailure || string.IsNullOrWhiteSpace(installUrlResult.Value))
                return Fail("Failed to get Retro Rewind download URL.");

            //todo, service
            var download = await downloads.DownloadDistributionAsync(installUrlResult.Value.Trim(), downloadedZipPath, operation);
            if (download.IsFailure)
                return operation.CancellationToken.IsCancellationRequested ? Ok() : download.Error;
            if (!_fileSystem.File.Exists(download.Value))
                return Fail("Failed to download Retro Rewind files.");
            downloadedZipPath = download.Value;

            // 2) Extract
            operation.Report(new(Message: t("state.extracting")));

            var extractResult = await Task.Run(() => ExtractZipFile(downloadedZipPath, tempExtractionPath, operation));

            if (extractResult.IsFailure)
            {
                result = extractResult;
                throw extractResult.Error.Exception ?? new Exception(extractResult.Error.Message);
            }

            // 3) Locate the extracted sub-folder
            var sourceFolder = _fileSystem.Path.Combine(tempExtractionPath, FolderName);
            if (!_fileSystem.Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException($"Could not find a '{FolderName}' folder inside {tempExtractionPath}");

            // 4) Remove existing install, if any
            var removeResult = await RemoveAsync(operation);
            if (removeResult.IsFailure)
            {
                result = removeResult;
                throw removeResult.Error.Exception ?? new Exception(removeResult.Error.Message);
            }

            // 5) Move over RetroRewind
            var xmlFolderSource = _fileSystem.Path.Combine(tempExtractionPath, XMLFolderName);
            var riivolutionFiles = _fileSystem.Directory.EnumerateFiles(xmlFolderSource, "*", SearchOption.AllDirectories);
            var retroRewindFiles = _fileSystem.Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories);
            foreach (var file in riivolutionFiles.Concat(retroRewindFiles))
            {
                var destinationPath = _fileSystem.Path.Combine(
                    destinationParentDir.FullName,
                    _fileSystem.Path.GetRelativePath(tempExtractionPath, file)
                );
                var destinationDirectoryName = _fileSystem.Path.GetDirectoryName(destinationPath);
                if (destinationDirectoryName != null)
                {
                    var directory = _fileSystem.DirectoryInfo.New(destinationDirectoryName);
                    if (!directory?.Exists ?? false)
                        directory?.Create();
                }
                _fileSystem.File.Move(file, destinationPath, false); //skip existing files for safety
            }
        }
        catch (Exception e)
        {
            result ??= Fail(e);
            _logger.LogError(e, e.Message);
        }
        finally
        {
            if (_fileSystem.File.Exists(downloadedZipPath))
                _fileSystem.File.Delete(downloadedZipPath);

            if (_fileSystem.Directory.Exists(tempExtractionPath))
                _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
        }
        return result ?? Ok();
    }

    private async Task BackupOldrksys()
    {
        var rrWfc = GetOldRksys();
        if (!_fileSystem.Directory.Exists(rrWfc))
            return;
        var rksysFiles = _fileSystem.Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
        if (rksysFiles.Length == 0)
            return;
        var sourceFile = rksysFiles[0];
        var regionFolder = _fileSystem.Path.GetDirectoryName(sourceFile);
        var regionFolderName = _fileSystem.Path.GetFileName(regionFolder);
        var datFileData = await _fileSystem.File.ReadAllBytesAsync(sourceFile);
        if (regionFolderName == null)
            return;
        var destinationFolder = _fileSystem.Path.Combine(_paths.SaveFolderPath, regionFolderName);
        _fileSystem.Directory.CreateDirectory(destinationFolder);
        var destinationFile = _fileSystem.Path.Combine(destinationFolder, "rksys.dat");
        await _fileSystem.File.WriteAllBytesAsync(destinationFile, datFileData);
    }

    private bool HasOldRksys()
    {
        return !string.IsNullOrWhiteSpace(GetOldRksys());
    }

    private string GetOldRksys()
    {
        // todo, maybe we should check for the existence of the file instead of the folder? and also find the oldest one?
        var rrWfcPaths = new[]
        {
            _paths.SaveFolderPath,
            // Also consider the folder with upper-case `Save`
            _fileSystem.Path.Combine(_paths.RootFolderPath, "riivolution", "Save", "RetroWFC"),
            _fileSystem.Path.Combine(_dolphinPaths.LoadFolderPath, "Riivolution", "save", "RetroWFC"),
            _fileSystem.Path.Combine(_dolphinPaths.LoadFolderPath, "Riivolution", "Save", "RetroWFC"),
            _fileSystem.Path.Combine(_dolphinPaths.LoadFolderPath, "riivolution", "save", "RetroWFC"),
            _fileSystem.Path.Combine(_dolphinPaths.LoadFolderPath, "riivolution", "Save", "RetroWFC"),
        };

        foreach (var rrWfc in rrWfcPaths)
        {
            if (!_fileSystem.Directory.Exists(rrWfc))
                continue;
            var rksysFiles = _fileSystem.Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
            if (rksysFiles.Length > 0)
                return rrWfc;
        }

        return string.Empty;
    }

    private async Task<OperationResult<bool>> IsRRUpToDate(SemVersion currentVersion)
    {
        var latestVersionResult = await LatestServerVersion();
        if (latestVersionResult.IsFailure)
            return Fail("Failed to check for updates");

        var latestVersion = latestVersionResult.Value;
        var isUpToDate = currentVersion.ComparePrecedenceTo(latestVersion) >= 0;
        return isUpToDate;
    }

    private async Task<OperationResult<SemVersion>> LatestServerVersion()
    {
        var response = await _api.CallApiAsync(api => api.GetVersionFile());
        if (!response.IsSuccess || String.IsNullOrWhiteSpace(response.Value))
            return Fail("Failed to check for updates");

        var result = response.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Split(' ')[0];
        return SemVersion.Parse(result);
    }

    public async Task<OperationResult> UpdateAsync(DistributionOperation operation)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            if (currentVersion == null)
                return await InstallAsync(operation);

            var isRRUpToDate = await IsRRUpToDate(currentVersion);
            if (isRRUpToDate.IsFailure)
                return isRRUpToDate;

            if (isRRUpToDate.Value)
                return Ok();

            //if current version is below 3.2.6 we need to do a full reinstall
            if (currentVersion.ComparePrecedenceTo(new SemVersion(3, 2, 6)) < 0)
            {
                var result = await ReinstallAsync(operation);
                return result.IsSuccess ? Ok() : result;
            }
            return await ApplyUpdates(currentVersion, operation);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private async Task<OperationResult> ApplyUpdates(SemVersion currentVersion, DistributionOperation operation)
    {
        var allVersions = await GetAllVersionData();
        var updatesToApply = GetUpdatesToApply(currentVersion, allVersions);
        // Step 1: Get the version we are updating to
        var targetVersion = updatesToApply.Any() ? updatesToApply.Last().Version : currentVersion;

        // Step 2: Apply file deletions for versions between current and targetVersion
        var deleteSuccess = await ApplyFileDeletionsBetweenVersions(currentVersion, targetVersion);
        if (deleteSuccess.IsFailure)
            return Fail(t("message_error.abort_rr.extra.failed_update_delete"));

        // Step 3: Download and apply the updates (if any)
        for (var i = 0; i < updatesToApply.Count; i++)
        {
            var update = updatesToApply[i];

            var success = await DownloadAndApplyUpdate(update, updatesToApply.Count, i + 1, operation);
            if (operation.CancellationToken.IsCancellationRequested)
                return Ok();

            if (success.IsFailure)
                return Fail(t("message_error.abort_rr.extra.failed_update_apply"));

            // Update the version file after each successful update
            UpdateVersionFile(update.Version);
        }
        return Ok();
    }

    private void UpdateVersionFile(SemVersion newVersion)
    {
        var versionFilePath = _fileSystem.Path.Combine(_paths.RootFolderPath, FolderName, "version.txt");
        _fileSystem.File.WriteAllText(versionFilePath, newVersion.ToString());
    }

    private async Task<OperationResult> DownloadAndApplyUpdate(
        UpdateData update,
        int totalUpdates,
        int currentUpdateIndex,
        DistributionOperation operation
    )
    {
        var tempZipPath = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(), _fileSystem.Path.GetRandomFileName());
        try
        {
            operation.Report(new(Message: $"{t("action.update")} {currentUpdateIndex}/{totalUpdates}: {update.Description}"));
            var download = await downloads.DownloadDistributionAsync(update.Url, tempZipPath, operation);
            if (download.IsFailure)
                return download.Error;
            var finalFile = download.Value;

            operation.Report(new(Percent: 100));
            operation.Report(new(Message: t("state.extracting")));
            var destinationDirectoryPath = _paths.RootFolderPath;
            _fileSystem.Directory.CreateDirectory(destinationDirectoryPath);
            var extractResult = ExtractZipFile(finalFile, destinationDirectoryPath, operation);
            if (extractResult.IsFailure)
                return extractResult;

            if (_fileSystem.File.Exists(finalFile))
                _fileSystem.File.Delete(finalFile);
        }
        finally
        {
            if (_fileSystem.File.Exists(tempZipPath))
                _fileSystem.File.Delete(tempZipPath);
        }

        return Ok();
    }

    private OperationResult ExtractZipFile(string path, string destinationDirectory, DistributionOperation operation)
    {
        using var archiveStream = _fileSystem.File.OpenRead(path);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);

        // 1) Compute total work units (weâ€™ll treat each entry as one â€œunitâ€)
        var entries = archive.Entries.Where(e => !e.FullName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)).ToList();
        var total = entries.Count;
        if (total == 0)
            return Ok();

        // Tell the UI what weâ€™re doing, and set a â€œgoalâ€ so it can estimate MB or items

        operation.Report(new(Message: t("state.extracting"), Goal: $"Extracting {total} files"));

        for (var i = 0; i < total; i++)
        {
            var entry = entries[i];
            if (!PathSafety.TryGetPathWithinDirectory(destinationDirectory, entry.FullName, out var destinationPath))
                return Fail("The file path is outside the destination directory. Please contact the developers.");

            // If itâ€™s a directory, create it
            if (entry.FullName.EndsWith(Path.AltDirectorySeparatorChar) || entry.FullName.EndsWith(Path.DirectorySeparatorChar))
            {
                _fileSystem.Directory.CreateDirectory(destinationPath);
            }
            else
            {
                // Ensure folder exists
                var dir = _fileSystem.Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(dir))
                    _fileSystem.Directory.CreateDirectory(dir);

                // Ensure read permission is set
                entry.ExternalAttributes |= Convert.ToInt32("644", 8) << 16;
                // Extract the file
                using var input = entry.Open();
                using var output = _fileSystem.File.Create(destinationPath);
                input.CopyTo(output);
            }

            // Report incremental progress (0â€“100)
            var percent = (int)(((i + 1) / (double)total) * 100);
            operation.Report(new(Percent: percent));
        }

        return Ok();
    }

    private async Task<OperationResult> ApplyFileDeletionsBetweenVersions(SemVersion currentVersion, SemVersion targetVersion)
    {
        try
        {
            var deleteListResult = await GetFileDeletionList();
            if (deleteListResult.IsFailure)
                return Fail("Failed to get file deletion list");

            var deleteList = deleteListResult.Value;
            var deletionsToApply = GetDeletionsToApply(currentVersion, targetVersion, deleteList);

            foreach (var file in deletionsToApply)
            {
                // The deletion list is server-controlled, so keep every resolved path inside the riivolution folder.
                if (!PathSafety.TryGetPathWithinDirectory(_paths.RootFolderPath, file.Path.TrimStart('/', '\\'), out var filePath))
                    return Fail("Invalid file path detected. Please contact the developers.\n Server error: " + file.Path);

                if (_fileSystem.File.Exists(filePath))
                    _fileSystem.File.Delete(filePath);
                else if (_fileSystem.Directory.Exists(filePath))
                    _fileSystem.Directory.Delete(filePath, recursive: true);
            }

            return Ok();
        }
        catch (Exception e)
        {
            return Fail($"Failed to delete files: {e.Message}");
        }
    }

    private struct DeletionData
    {
        public SemVersion Version;
        public string Path;
    }

    private async Task<OperationResult<List<DeletionData>>> GetFileDeletionList()
    {
        var deleteList = new List<DeletionData>();

        var deleteListOperation = await _api.CallApiAsync(api => api.GetDeletionFile());
        if (deleteListOperation.IsFailure)
            return Fail("Failed to get file deletion list");

        var lines = deleteListOperation.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(' ', 2);
            if (parts.Length < 2)
                continue;
            var deletionVersion = parts[0].Trim();
            var path = parts[1].Trim();
            if (string.IsNullOrWhiteSpace(deletionVersion) || string.IsNullOrWhiteSpace(path))
                continue;
            if (!SemVersion.TryParse(deletionVersion, out var parsedVersion))
                return Fail("Failed to parse version");

            var deletionData = new DeletionData { Version = parsedVersion, Path = path };
            deleteList.Add(deletionData);
        }

        return deleteList;
    }

    private struct UpdateData
    {
        public SemVersion Version;
        public string Url;
        public string Description;
    }

    private async Task<List<UpdateData>> GetAllVersionData()
    {
        var versions = new List<UpdateData>();

        var allVersionsResult = await _api.CallApiAsync(api => api.GetVersionFile());
        if (allVersionsResult.IsFailure)
            return new();
        var lines = allVersionsResult.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split(' ', 4);
            if (parts.Length < 4)
                continue;
            var version = parts[0].Trim();
            var url = parts[1].Trim();
            var path = parts[2].Trim(); // Path unused in our program since on pc we manually decide where to extract
            var description = parts[3].Trim();
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path))
                continue;
            // Fix old URLs using HTTP to the new endpoint
            var fixedUrl = url.Replace(RetroRewindEndpoints.LegacyBaseUrl, RetroRewindEndpoints.BaseUrl);
            if (!SemVersion.TryParse(version, out var _))
                continue;
            var parsedVersion = SemVersion.Parse(version);
            var updateData = new UpdateData
            {
                Version = parsedVersion,
                Url = fixedUrl,
                Description = description,
            };
            versions.Add(updateData);
        }
        return versions;
    }

    //todo: see if we can make this generic to the point we dont have to split up deletions and updates
    private static List<UpdateData> GetUpdatesToApply(SemVersion currentVersion, List<UpdateData> allVersions)
    {
        var updatesToApply = new List<UpdateData>();
        foreach (var update in allVersions)
        {
            if (update.Version.ComparePrecedenceTo(currentVersion) > 0)
                updatesToApply.Add(update);
        }
        return updatesToApply;
    }

    private static List<DeletionData> GetDeletionsToApply(
        SemVersion currentVersion,
        SemVersion targetVersion,
        List<DeletionData> allDeletions
    )
    {
        var deletionsToApply = new List<DeletionData>();
        allDeletions = allDeletions
            .OrderByDescending(d => d.Version, Comparer<SemVersion>.Create((a, b) => a.ComparePrecedenceTo(b)))
            .ToList();
        foreach (var deletion in allDeletions)
        {
            if (deletion.Version.ComparePrecedenceTo(currentVersion) > 0 && deletion.Version.ComparePrecedenceTo(targetVersion) <= 0)
                deletionsToApply.Add(deletion);
        }

        deletionsToApply.Reverse();
        return deletionsToApply;
    }

    public Task<OperationResult> RemoveAsync(DistributionOperation operation)
    {
        //where the RR distribution lives
        var distributionDataDestination = _fileSystem.Path.Combine(_paths.RootFolderPath, FolderName);
        //where the RR wiiDisc xml file lives
        var riivolutionDiscXMLFile = _fileSystem.Path.Combine(_paths.RootFolderPath, XMLFolderName, $"{XMLFileName}.xml");

        if (_fileSystem.Directory.Exists(distributionDataDestination))
            _fileSystem.Directory.Delete(distributionDataDestination, recursive: true);
        if (_fileSystem.File.Exists(riivolutionDiscXMLFile))
            _fileSystem.File.Delete(riivolutionDiscXMLFile);

        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> ReinstallAsync(DistributionOperation operation)
    {
        //Remove and install
        var removeResult = await RemoveAsync(operation);
        if (removeResult.IsFailure)
            return removeResult;

        return await InstallAsync(operation);
    }

    public async Task<OperationResult<WheelWizardStatus>> GetCurrentStatusAsync()
    {
        if (!_settingsManager.PathsSetupCorrectly())
            return WheelWizardStatus.ConfigNotFinished;

        var serverEnabled = await _api.CallApiAsync(api => api.Ping());
        var rrInstalled = GetCurrentVersion() != null;

        if (serverEnabled.IsFailure)
            return rrInstalled ? WheelWizardStatus.NoServerButInstalled : WheelWizardStatus.NoServer;

        if (!rrInstalled)
            return WheelWizardStatus.NotInstalled;

        var currentVersion = GetCurrentVersion();
        if (currentVersion == null)
            return WheelWizardStatus.NotInstalled;

        var retroRewindUpToDateResult = await IsRRUpToDate(currentVersion);
        if (retroRewindUpToDateResult.IsFailure)
            return Fail("Failed to check for updates");

        var retroRewindUpToDate = retroRewindUpToDateResult.Value;
        return !retroRewindUpToDate ? WheelWizardStatus.OutOfDate : WheelWizardStatus.Ready;
    }

    public SemVersion? GetCurrentVersion()
    {
        var versionFilePath = _fileSystem.Path.Combine(_paths.RootFolderPath, FolderName, "version.txt");
        if (!_fileSystem.File.Exists(versionFilePath))
            return null;

        var versionText = _fileSystem.File.ReadAllText(versionFilePath).Trim();
        var versionPattern = @"^\d+\.\d+\.\d+$";
        if (!Regex.IsMatch(versionText, versionPattern))
            return null;

        return SemVersion.Parse(versionText);
    }
}
