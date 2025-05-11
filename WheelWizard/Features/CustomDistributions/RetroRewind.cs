using System.IO.Compression;
using System.Text.RegularExpressions;
using Semver;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.CustomDistributions;

public class RetroRewind : IDistribution
{
    public string Title => "Retro Rewind";

    // Keep in mind, whenever we download update files from the server, they are actually 1 folder higher, so it contains this folder.
    public string FolderName => "RetroRewind6";

    public async Task<OperationResult> Install()
    {
        if (GetCurrentVersion() is null)
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
        var serverResponse = await HttpClientHelper.GetAsync<string>(Endpoints.RRUrl);
        if (!serverResponse.Succeeded)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Could not connect to the server")
                .SetInfoText(Phrases.PopupText_CouldNotConnectServer)
                .ShowDialog();
            return "Could not connect to the server";
        }
        await DownloadAndExtractRetroRewind(PathManager.RetroRewindTempFile);
        await Update();
        return Ok();
    }

    private static async Task DownloadAndExtractRetroRewind(string tempZipPath)
    {
        var progressWindow = new ProgressWindow(Phrases.PopupText_InstallingRR);
        progressWindow.SetExtraText(Phrases.PopupText_InstallingRRFirstTime);
        progressWindow.Show();

        try
        {
            await DownloadHelper.DownloadToLocationAsync(Endpoints.RRZipUrl, tempZipPath, progressWindow);
            progressWindow.SetExtraText(Common.State_Extracting);
            var extractionPath = PathManager.RiivolutionWhWzFolderPath;
            ZipFile.ExtractToDirectory(tempZipPath, extractionPath, true);
        }
        finally
        {
            progressWindow.Close();
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }
    }

    private static async Task BackupOldrksys()
    {
        var rrWfc = GetOldRksys();
        if (!Directory.Exists(rrWfc))
            return;
        var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
        if (rksysFiles.Length == 0)
            return;
        var sourceFile = rksysFiles[0];
        var regionFolder = Path.GetDirectoryName(sourceFile);
        var regionFolderName = Path.GetFileName(regionFolder);
        var datFileData = await File.ReadAllBytesAsync(sourceFile);
        if (regionFolderName == null)
            return;
        var destinationFolder = Path.Combine(PathManager.SaveFolderPath, regionFolderName);
        Directory.CreateDirectory(destinationFolder);
        var destinationFile = Path.Combine(destinationFolder, "rksys.dat");
        await File.WriteAllBytesAsync(destinationFile, datFileData);
    }

    private static bool HasOldRksys()
    {
        return !string.IsNullOrWhiteSpace(GetOldRksys());
    }

    private static string GetOldRksys()
    {
        var rrWfcPaths = new[]
        {
            Path.Combine(PathManager.SaveFolderPath),
            // Also consider the folder with upper-case `Save`
            Path.Combine(PathManager.RiivolutionWhWzFolderPath, "riivolution", "Save", "RetroWFC"),
            Path.Combine(PathManager.LoadFolderPath, "Riivolution", "save", "RetroWFC"),
            Path.Combine(PathManager.LoadFolderPath, "Riivolution", "Save", "RetroWFC"),
            Path.Combine(PathManager.LoadFolderPath, "riivolution", "save", "RetroWFC"),
            Path.Combine(PathManager.LoadFolderPath, "riivolution", "Save", "RetroWFC"),
        };

        foreach (var rrWfc in rrWfcPaths)
        {
            if (!Directory.Exists(rrWfc))
                continue;
            var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
            if (rksysFiles.Length > 0)
                return rrWfc;
        }

        return string.Empty;
    }

    private static async Task<OperationResult<bool>> IsRRUpToDate(SemVersion currentVersion)
    {
        var latestVersionResult = await LatestServerVersion();
        if (latestVersionResult.IsFailure)
            return "Failed to check for updates";
        var latestVersion = latestVersionResult.Value;
        var isUpToDate = currentVersion.ComparePrecedenceTo(latestVersion) >= 0;
        return isUpToDate;
    }

    private static async Task<OperationResult<SemVersion>> LatestServerVersion()
    {
        var response = await HttpClientHelper.GetAsync<string>(Endpoints.RRVersionUrl);
        if (response.Succeeded && response.Content != null)
        {
            var result = response.Content.Split('\n').Last().Split(' ')[0];
            return SemVersion.Parse(result);
        }

        return "Failed to check for updates";
    }

    public async Task<OperationResult> Update()
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
                //todo: look at this logic
                var result = await Install();
                if (result.IsFailure)
                    return result;
            }
            return await ApplyUpdates(currentVersion);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static async Task<OperationResult> ApplyUpdates(SemVersion currentVersion)
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

    private static void UpdateVersionFile(SemVersion newVersion)
    {
        var versionFilePath = Path.Combine(PathManager.RetroRewind6FolderPath, "version.txt");
        File.WriteAllText(versionFilePath, newVersion.ToString());
    }

    private static async Task<OperationResult> DownloadAndApplyUpdate(
        UpdateData update,
        int totalUpdates,
        int currentUpdateIndex,
        ProgressWindow popupWindow
    )
    {
        var tempZipPath = Path.GetTempFileName();
        try
        {
            popupWindow.SetExtraText($"{Common.Action_Update} {currentUpdateIndex}/{totalUpdates}: {update.Description}");
            var finalFile = await DownloadHelper.DownloadToLocationAsync(update.Url, tempZipPath, popupWindow);

            popupWindow.UpdateProgress(100);
            popupWindow.SetExtraText(Common.State_Extracting);
            var destinationDirectoryPath = PathManager.RiivolutionWhWzFolderPath;
            Directory.CreateDirectory(destinationDirectoryPath);
            ExtractZipFile(finalFile, destinationDirectoryPath);
            if (File.Exists(finalFile))
                File.Delete(finalFile);
        }
        finally
        {
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }

        return Ok();
    }

    private static OperationResult ExtractZipFile(string path, string destinationDirectory)
    {
        using var archive = ZipFile.OpenRead(path);

        // Absolute path of the destination directory
        var absoluteDestinationPath = Path.GetFullPath(destinationDirectory + Path.AltDirectorySeparatorChar);

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
                continue; // Skip the desktop.ini file

            // Get the full path of the file
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));

            // Check for directory traversal attacks
            if (!destinationPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal))
            {
                return ("The file path is outside the destination directory. Please contact the developers.");
            }

            // If the entry is a directory, create it
            if (entry.FullName.EndsWith(Path.AltDirectorySeparatorChar))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            // Create directory if it doesn't exist
            var directoryName = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directoryName))
                Directory.CreateDirectory(directoryName);

            // Extract the file
            entry.ExtractToFile(destinationPath, overwrite: true);
        }

        return Ok();
    }

    private static async Task<OperationResult> ApplyFileDeletionsBetweenVersions(SemVersion currentVersion, SemVersion targetVersion)
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
                var absoluteDestinationPath = Path.GetFullPath(PathManager.RiivolutionWhWzFolderPath + Path.AltDirectorySeparatorChar);
                var filePath = Path.GetFullPath(Path.Combine(absoluteDestinationPath, file.Path.TrimStart('/')));
                //because we are actually getting the path from the server,
                //we need to make sure we are not getting hacked, so we check if the path is in the riivolution folder
                var resolvedPath = Path.GetFullPath(new FileInfo(filePath).FullName);
                if (
                    !resolvedPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
                    || !filePath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
                    || file.Path.Contains("..")
                )
                {
                    return "Invalid file path detected. Please contact the developers.\n Server error: " + resolvedPath;
                }

                if (File.Exists(filePath))
                    File.Delete(filePath);
                else if (Directory.Exists(filePath))
                    Directory.Delete(filePath, recursive: true);
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

    private static async Task<OperationResult<List<DeletionData>>> GetFileDeletionList()
    {
        var deleteList = new List<DeletionData>();

        using var httpClient = new HttpClient();
        var deleteListText = await httpClient.GetStringAsync(Endpoints.RRVersionDeleteUrl);
        var lines = deleteListText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

    private static async Task<List<UpdateData>> GetAllVersionData()
    {
        var versions = new List<UpdateData>();

        using var httpClient = new HttpClient();
        var allVersionsText = await httpClient.GetStringAsync(Endpoints.RRVersionUrl);
        var lines = allVersionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

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

    public Task<OperationResult> Remove()
    {
        var retroRewindPath = PathManager.RetroRewind6FolderPath;
        if (Directory.Exists(retroRewindPath))
            Directory.Delete(retroRewindPath, true);
        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> Reinstall()
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

        var serverEnabled = await HttpClientHelper.GetAsync<string>(Endpoints.RRUrl);
        var rrInstalled = GetCurrentVersion() != null;

        if (!serverEnabled.Succeeded)
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
        if (!File.Exists(versionFilePath))
            return null;

        var versionText = File.ReadAllText(versionFilePath).Trim();
        var versionPattern = @"^\d+\.\d+\.\d+$";
        if (!Regex.IsMatch(versionText, versionPattern))
            return null;

        return SemVersion.Parse(versionText);
    }
}
