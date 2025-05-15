using System.IO.Abstractions;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Semver;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.Services;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.CustomDistributions;

public class RetroRewind : IDistribution
{
    private readonly IFileSystem _fileSystem;
    private readonly IApiCaller<IRetroRewindApi> _api;

    public RetroRewind(IFileSystem fileSystem, IApiCaller<IRetroRewindApi> api)
    {
        _api = api;
        _fileSystem = fileSystem;
    }

    public string Title => "Retro Rewind";

    // Keep in mind, whenever we download update files from the server, they are actually 1 folder higher, so it contains this folder.
    public string FolderName => "RetroRewind6";

    public async Task<OperationResult> Install(ProgressWindow? progressWindow = null)
    {
        if (GetCurrentVersion() is not null)
        {
            var removeResult = await Remove();
            if (removeResult.IsFailure)
                return removeResult;
        }

        if (HasOldRksys())
        {
            var rksysQuestion = new YesNoWindow()
                .SetMainText(Phrases.PopupText_OldRksysFound)
                .SetExtraText(Phrases.PopupText_OldRksysFoundExplained);
            if (await rksysQuestion.AwaitAnswer())
                await BackupOldrksys();
        }
        var serverResponse = await _api.CallApiAsync(api => api.Ping()); // actual response doesnt matter
        if (serverResponse.IsFailure)
        {
            return "Could not connect to the server";
        }
        var downloadResult = await DownloadAndExtractRetroRewind();
        if (downloadResult.IsFailure)
            return downloadResult;
        var updateResult = await Update();
        if (updateResult.IsFailure)
            return updateResult;
        return Ok();
    }

    private async Task<OperationResult> DownloadAndExtractRetroRewind(ProgressWindow? progressWindow = null)
    {
        if (progressWindow is not null)
        {
            progressWindow.SetExtraText(Phrases.PopupText_InstallingRRFirstTime);
            progressWindow.Show();
        }

        // path to the downloaded .zip
        var tempZipPath = PathManager.RetroRewindTempFile;
        // where we'll do the extraction
        var tempExtractionPath = PathManager.TempModsFolderPath;
        // where the final RR folder should live
        var finalDestination = _fileSystem.Path.Combine(PathManager.RiivolutionWhWzFolderPath, FolderName);

        try
        {
            // 1) Download
            if (_fileSystem.Directory.Exists(tempExtractionPath))
                _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
            _fileSystem.Directory.CreateDirectory(tempExtractionPath);

            await DownloadHelper.DownloadToLocationAsync(Endpoints.RRZipUrl, tempZipPath, progressWindow);

            // 2) Extract
            if (progressWindow is not null)
            {
                progressWindow.SetExtraText(Common.State_Extracting);
            }
            
            ZipFile.ExtractToDirectory(tempZipPath, tempExtractionPath, overwriteFiles: true);

            // 3) Locate the extracted sub-folder
            var sourceFolder = _fileSystem.Path.Combine(tempExtractionPath, FolderName);
            if (!_fileSystem.Directory.Exists(sourceFolder))
            {
                var directories = _fileSystem.Directory.GetDirectories(tempExtractionPath);
                if (directories.Length == 1)
                    sourceFolder = directories[0];
                else
                    return new DirectoryNotFoundException($"Could not find a '{FolderName}' folder inside {tempExtractionPath}");
            }

            // 4) Replace existing install, if any
            if (_fileSystem.Directory.Exists(finalDestination))
                _fileSystem.Directory.Delete(finalDestination, recursive: true);

            // 5) Move into place
            _fileSystem.Directory.Move(sourceFolder, finalDestination);
        }
        finally
        {
            // always clean up UI and temp files
            progressWindow?.Close();

            if (_fileSystem.File.Exists(tempZipPath))
                _fileSystem.File.Delete(tempZipPath);

            if (_fileSystem.Directory.Exists(tempExtractionPath))
                _fileSystem.Directory.Delete(tempExtractionPath, recursive: true);
        }
        return Ok();
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
        var destinationFolder = _fileSystem.Path.Combine(PathManager.SaveFolderPath, regionFolderName);
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
            _fileSystem.Path.Combine(PathManager.SaveFolderPath),
            // Also consider the folder with upper-case `Save`
            _fileSystem.Path.Combine(PathManager.RiivolutionWhWzFolderPath, "riivolution", "Save", "RetroWFC"),
            _fileSystem.Path.Combine(PathManager.LoadFolderPath, "Riivolution", "save", "RetroWFC"),
            _fileSystem.Path.Combine(PathManager.LoadFolderPath, "Riivolution", "Save", "RetroWFC"),
            _fileSystem.Path.Combine(PathManager.LoadFolderPath, "riivolution", "save", "RetroWFC"),
            _fileSystem.Path.Combine(PathManager.LoadFolderPath, "riivolution", "Save", "RetroWFC"),
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
            return "Failed to check for updates";
        var latestVersion = latestVersionResult.Value;
        var isUpToDate = currentVersion.ComparePrecedenceTo(latestVersion) >= 0;
        return isUpToDate;
    }

    private async Task<OperationResult<SemVersion>> LatestServerVersion()
    {
        var response = await _api.CallApiAsync(api => api.GetVersionFile());
        if (!response.IsSuccess || String.IsNullOrWhiteSpace(response.Value))
            return "Failed to check for updates";

        var result = response.Value.Split('\n').Last().Split(' ')[0];
        return SemVersion.Parse(result);
    }

    public async Task<OperationResult> Update(ProgressWindow? progressWindow = null)
    {
        try
        {
            var currentVersion = GetCurrentVersion();
            if (currentVersion == null)
                return await Install();

            var isRRUpToDate = await IsRRUpToDate(currentVersion);
            if (isRRUpToDate.IsFailure)
                return isRRUpToDate;

            if (isRRUpToDate.Value)
            {
                return Ok();
            }

            //if current version is below 3.2.6 we need to do a full reinstall
            if (currentVersion.ComparePrecedenceTo(new SemVersion(3, 2, 6)) < 0)
            {
                var result = await Reinstall();
                return result.IsSuccess ? Ok() : result;
            }
            return await ApplyUpdates(currentVersion);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private async Task<OperationResult> ApplyUpdates(SemVersion currentVersion)
    {
        var allVersions = await GetAllVersionData();
        var updatesToApply = GetUpdatesToApply(currentVersion, allVersions);

        // todo: This progressbar should not be here in this context, this makes this untestable
        var progressWindow = new ProgressWindow(Phrases.PopupText_UpdateRR);
        progressWindow.Show();

        // Step 1: Get the version we are updating to
        var targetVersion = updatesToApply.Any() ? updatesToApply.Last().Version : currentVersion;

        // Step 2: Apply file deletions for versions between current and targetVersion
        var deleteSuccess = await ApplyFileDeletionsBetweenVersions(currentVersion, targetVersion);
        if (deleteSuccess.IsFailure)
        {
            progressWindow.Close();
            return (Phrases.PopupText_FailedUpdateDelete);
        }

        // Step 3: Download and apply the updates (if any)
        for (var i = 0; i < updatesToApply.Count; i++)
        {
            var update = updatesToApply[i];

            var success = await DownloadAndApplyUpdate(update, updatesToApply.Count, i + 1, progressWindow);
            if (success.IsFailure)
            {
                progressWindow.Close();
                return (Phrases.PopupText_FailedUpdateApply);
            }

            // Update the version file after each successful update
            UpdateVersionFile(update.Version);
        }

        progressWindow.Close();
        return Ok();
    }

    private void UpdateVersionFile(SemVersion newVersion)
    {
        var versionFilePath = _fileSystem.Path.Combine(PathManager.RetroRewind6FolderPath, "version.txt");
        _fileSystem.File.WriteAllText(versionFilePath, newVersion.ToString());
    }

    private async Task<OperationResult> DownloadAndApplyUpdate(
        UpdateData update,
        int totalUpdates,
        int currentUpdateIndex,
        ProgressWindow popupWindow
    )
    {
        var tempZipPath = _fileSystem.Path.GetTempFileName();
        try
        {
            popupWindow.SetExtraText($"{Common.Action_Update} {currentUpdateIndex}/{totalUpdates}: {update.Description}");
            var finalFile = await DownloadHelper.DownloadToLocationAsync(update.Url, tempZipPath, popupWindow);

            popupWindow.UpdateProgress(100);
            popupWindow.SetExtraText(Common.State_Extracting);
            var destinationDirectoryPath = PathManager.RiivolutionWhWzFolderPath;
            _fileSystem.Directory.CreateDirectory(destinationDirectoryPath);
            ExtractZipFile(finalFile, destinationDirectoryPath);
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

    private OperationResult ExtractZipFile(string path, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(path);

        // Absolute path of the destination directory
        var absoluteDestinationPath = _fileSystem.Path.GetFullPath(destinationDirectory + Path.AltDirectorySeparatorChar);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue; // Skip the desktop.ini file

            // Get the full path of the file
            var destinationPath = _fileSystem.Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));

            // Check for directory traversal attacks
            if (!destinationPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal))
            {
                return ("The file path is outside the destination directory. Please contact the developers.");
            }

            // If the entry is a directory, create it
            if (entry.FullName.EndsWith(_fileSystem.Path.AltDirectorySeparatorChar))
            {
                _fileSystem.Directory.CreateDirectory(destinationPath);
                continue;
            }

            // Create directory if it doesn't exist
            var directoryName = _fileSystem.Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directoryName))
                _fileSystem.Directory.CreateDirectory(directoryName);

            // Extract the file
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return Ok();
    }

    private async Task<OperationResult> ApplyFileDeletionsBetweenVersions(SemVersion currentVersion, SemVersion targetVersion)
    {
        try
        {
            var deleteListResult = await GetFileDeletionList();
            if (deleteListResult.IsFailure)
            {
                return "Failed to get file deletion list";
            }
            var deleteList = deleteListResult.Value;
            var deletionsToApply = GetDeletionsToApply(currentVersion, targetVersion, deleteList);

            foreach (var file in deletionsToApply)
            {
                var absoluteDestinationPath = _fileSystem.Path.GetFullPath(
                    PathManager.RiivolutionWhWzFolderPath + _fileSystem.Path.AltDirectorySeparatorChar
                );
                var filePath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(absoluteDestinationPath, file.Path.TrimStart('/')));
                //because we are actually getting the path from the server,
                //we need to make sure we are not getting hacked, so we check if the path is in the riivolution folder
                var resolvedPath = _fileSystem.Path.GetFullPath(new FileInfo(filePath).FullName);
                if (
                    !resolvedPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
                    || !filePath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
                    || file.Path.Contains("..")
                )
                {
                    return "Invalid file path detected. Please contact the developers.\n Server error: " + resolvedPath;
                }

                if (_fileSystem.File.Exists(filePath))
                    _fileSystem.File.Delete(filePath);
                else if (_fileSystem.Directory.Exists(filePath))
                    _fileSystem.Directory.Delete(filePath, recursive: true);
            }

            return Ok();
        }
        catch (Exception e)
        {
            return ($"Failed to delete files: {e.Message}");
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
            return "Failed to get file deletion list";
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
                return "Failed to parse version";
            var deletionData = new DeletionData { Version = parsedVersion, Path = path };
            deleteList.Add(deletionData);
        }

        return deleteList;
    }

    private struct UpdateData
    {
        public SemVersion Version;
        public string Url;
        public string Path;
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
            var path = parts[2].Trim();
            var description = parts[3].Trim();
            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(path))
                continue;
            if (!SemVersion.TryParse(version, out var _))
                continue;
            var parsedVersion = SemVersion.Parse(version);
            var updateData = new UpdateData
            {
                Version = parsedVersion,
                Url = url,
                Path = path,
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
            {
                updatesToApply.Add(update);
            }
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
            {
                deletionsToApply.Add(deletion);
            }
        }

        deletionsToApply.Reverse();
        return deletionsToApply;
    }

    public Task<OperationResult> Remove(ProgressWindow? progressWindow = null)
    {
        var retroRewindPath = PathManager.RetroRewind6FolderPath;
        if (_fileSystem.Directory.Exists(retroRewindPath))
            _fileSystem.Directory.Delete(retroRewindPath, true);
        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> Reinstall(ProgressWindow? progressWindow = null)
    {
        //Remove and install
        var removeResult = await Remove();
        if (removeResult.IsFailure)
            return removeResult;
        return await Install();
    }

    public async Task<OperationResult<WheelWizardStatus>> GetCurrentStatus()
    {
        if (!SettingsHelper.PathsSetupCorrectly())
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
            return "Failed to check for updates";
        var retroRewindUpToDate = retroRewindUpToDateResult.Value;
        return !retroRewindUpToDate ? WheelWizardStatus.OutOfDate : WheelWizardStatus.Ready;
    }

    public SemVersion? GetCurrentVersion()
    {
        var versionFilePath = PathManager.RetroRewindVersionFile;
        if (!_fileSystem.File.Exists(versionFilePath))
            return null;

        var versionText = _fileSystem.File.ReadAllText(versionFilePath).Trim();
        var versionPattern = @"^\d+\.\d+\.\d+$";
        if (!Regex.IsMatch(versionText, versionPattern))
            return null;

        return SemVersion.Parse(versionText);
    }
}
