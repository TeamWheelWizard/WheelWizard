// using System.IO.Compression;
// using WheelWizard.Helpers;
// using WheelWizard.Resources.Languages;
// using WheelWizard.Views.Popups.Generic;
//
// namespace WheelWizard.Services.Installation;
//
// public static class RetroRewindUpdater
// {
//     public static async Task<bool> IsRRUpToDate(string currentVersion)
//     {
//         var latestVersion = await GetLatestVersionString();
//         return currentVersion.Trim() == latestVersion.Trim();
//     }
//
//     private static async Task<string> GetLatestVersionString()
//     {
//         var response = await HttpClientHelper.GetAsync<string>(Endpoints.RRVersionUrl);
//         if (response.Succeeded && response.Content != null)
//             return response.Content.Split('\n').Last().Split(' ')[0];
//         new YesNoWindow().SetMainText(Phrases.PopupText_FailCheckUpdates).AwaitAnswer();
//         return "Failed to check for updates";
//     }
//
//     public static async Task<bool> UpdateRR()
//     {
//         try
//         {
//             if (!RetroRewindInstaller.IsRetroRewindInstalled())
//                 return await RetroRewindInstaller.HandleNotInstalled();
//
//             var currentVersion = RetroRewindInstaller.CurrentRRVersion();
//             if (await IsRRUpToDate(currentVersion))
//             {
//                 await new MessageBoxWindow()
//                     .SetMessageType(MessageBoxWindow.MessageType.Message)
//                     .SetTitleText(Phrases.PopupText_RRUpToDate)
//                     .SetInfoText(Phrases.PopupText_RRUpToDate)
//                     .ShowDialog();
//                 return true;
//             }
//
//             //if current version is below 3.2.6 we need to do a full reinstall
//             if (CompareVersions(currentVersion, "3.2.6") < 0)
//                 return await RetroRewindInstaller.HandleOldVersion();
//             return await ApplyUpdates(currentVersion);
//         }
//         catch (Exception e)
//         {
//             AbortingUpdate($"Reason: {e.Message}");
//             return false;
//         }
//     }
//
//     private static async Task<bool> ApplyUpdates(string currentVersion)
//     {
//         var allVersions = await GetAllVersionData();
//         var updatesToApply = GetUpdatesToApply(currentVersion, allVersions);
//
//         var progressWindow = new ProgressWindow(Phrases.PopupText_UpdateRR);
//         progressWindow.Show();
//
//         // Step 1: Get the version we are updating to
//         var targetVersion = updatesToApply.Any() ? updatesToApply.Last().Version : currentVersion;
//
//         // Step 2: Apply file deletions for versions between current and targetVersion
//         var deleteSuccess = await ApplyFileDeletionsBetweenVersions(currentVersion, targetVersion);
//         if (!deleteSuccess)
//         {
//             AbortingUpdate(Phrases.PopupText_FailedUpdateDelete);
//             progressWindow.Close();
//             return false;
//         }
//
//         // Step 3: Download and apply the updates (if any)
//         for (var i = 0; i < updatesToApply.Count; i++)
//         {
//             var update = updatesToApply[i];
//
//             var success = await DownloadAndApplyUpdate(update, updatesToApply.Count, i + 1, progressWindow);
//             if (!success)
//             {
//                 progressWindow.Close();
//                 AbortingUpdate(Phrases.PopupText_FailedUpdateApply);
//                 return false;
//             }
//
//             // Update the version file after each successful update
//             UpdateVersionFile(update.Version);
//         }
//
//         progressWindow.Close();
//         return true;
//     }
//
//     private static async Task<bool> ApplyFileDeletionsBetweenVersions(string currentVersion, string targetVersion)
//     {
//         try
//         {
//             var deleteList = await GetFileDeletionList();
//             var deletionsToApply = GetDeletionsToApply(currentVersion, targetVersion, deleteList);
//
//             foreach (var file in deletionsToApply)
//             {
//                 var absoluteDestinationPath = Path.GetFullPath(PathManager.RiivolutionWhWzFolderPath + Path.AltDirectorySeparatorChar);
//                 var filePath = Path.GetFullPath(Path.Combine(absoluteDestinationPath, file.Path.TrimStart('/')));
//                 //because we are actually getting the path from the server,
//                 //we need to make sure we are not getting hacked, so we check if the path is in the riivolution folder
//                 var resolvedPath = Path.GetFullPath(new FileInfo(filePath).FullName);
//                 if (
//                     !resolvedPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
//                     || !filePath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal)
//                     || file.Path.Contains("..")
//                 )
//                 {
//                     AbortingUpdate("Invalid file path detected. Please contact the developers.\n Server error: " + resolvedPath);
//                     return false;
//                 }
//
//                 if (File.Exists(filePath))
//                     File.Delete(filePath);
//                 else if (Directory.Exists(filePath))
//                     Directory.Delete(filePath, recursive: true);
//             }
//
//             return true;
//         }
//         catch (Exception e)
//         {
//             AbortingUpdate($"Failed to delete files: {e.Message}");
//             return false;
//         }
//     }
//
//     private static List<(string Version, string Path)> GetDeletionsToApply(
//         string currentVersion,
//         string targetVersion,
//         List<(string Version, string Path)> allDeletions
//     )
//     {
//         var deletionsToApply = new List<(string Version, string Path)>();
//         allDeletions.Sort((a, b) => CompareVersions(b.Version, a.Version)); // Sort in descending order
//         foreach (var deletion in allDeletions)
//         {
//             if (CompareVersions(deletion.Version, currentVersion) > 0 && CompareVersions(deletion.Version, targetVersion) <= 0)
//             {
//                 deletionsToApply.Add(deletion);
//             }
//         }
//
//         deletionsToApply.Reverse();
//         return deletionsToApply;
//     }
//
//     private static async Task<List<(string Version, string Path)>> GetFileDeletionList()
//     {
//         var deleteList = new List<(string Version, string Path)>();
//
//         using var httpClient = new HttpClient();
//         var deleteListText = await httpClient.GetStringAsync(Endpoints.RRVersionDeleteUrl);
//         var lines = deleteListText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
//
//         foreach (var line in lines)
//         {
//             var parts = line.Split(' ', 2);
//             if (parts.Length < 2)
//                 continue;
//             deleteList.Add((parts[0].Trim(), parts[1].Trim()));
//         }
//
//         return deleteList;
//     }
//
//     private static void UpdateVersionFile(string newVersion)
//     {
//         var versionFilePath = Path.Combine(PathManager.RetroRewind6FolderPath, "version.txt");
//         File.WriteAllText(versionFilePath, newVersion);
//     }
//
//     private static async Task<List<(string Version, string Url, string Path, string Description)>> GetAllVersionData()
//     {
//         var versions = new List<(string Version, string Url, string Path, string Description)>();
//
//         using var httpClient = new HttpClient();
//         var allVersionsText = await httpClient.GetStringAsync(Endpoints.RRVersionUrl);
//         var lines = allVersionsText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
//
//         foreach (var line in lines)
//         {
//             var parts = line.Split(' ', 4);
//             if (parts.Length < 4)
//                 continue;
//             versions.Add((parts[0].Trim(), parts[1].Trim(), parts[2].Trim(), parts[3].Trim()));
//         }
//
//         return versions;
//     }
//
//     private static List<(string Version, string Url, string Path, string Description)> GetUpdatesToApply(
//         string currentVersion,
//         List<(string Version, string Url, string Path, string Description)> allVersions
//     )
//     {
//         var updatesToApply = new List<(string Version, string Url, string Path, string Description)>();
//         allVersions.Sort((a, b) => CompareVersions(b.Version, a.Version)); // Sort in descending order
//         foreach (var version in allVersions)
//         {
//             if (CompareVersions(version.Version, currentVersion) > 0)
//                 updatesToApply.Add(version);
//             else
//                 break;
//         }
//
//         updatesToApply.Reverse();
//         return updatesToApply;
//     }
//
//     private static int CompareVersions(string v1, string v2)
//     {
//         var parts1 = v1.Split('.').Select(int.Parse).ToArray();
//         var parts2 = v2.Split('.').Select(int.Parse).ToArray();
//         for (var i = 0; i < Math.Max(parts1.Length, parts2.Length); i++)
//         {
//             var p1 = i < parts1.Length ? parts1[i] : 0;
//             var p2 = i < parts2.Length ? parts2[i] : 0;
//             if (p1 != p2)
//                 return p1.CompareTo(p2);
//         }
//
//         return 0;
//     }
//
//     private static async Task<bool> DownloadAndApplyUpdate(
//         (string Version, string Url, string Path, string Description) update,
//         int totalUpdates,
//         int currentUpdateIndex,
//         ProgressWindow popupWindow
//     )
//     {
//         var tempZipPath = Path.GetTempFileName();
//         try
//         {
//             popupWindow.SetExtraText($"{Common.Action_Update} {currentUpdateIndex}/{totalUpdates}: {update.Description}");
//             var finalFile = await DownloadHelper.DownloadToLocationAsync(update.Url, tempZipPath, popupWindow);
//
//             popupWindow.UpdateProgress(100);
//             popupWindow.SetExtraText(Common.State_Extracting);
//             var destinationDirectoryPath = PathManager.RiivolutionWhWzFolderPath;
//             Directory.CreateDirectory(destinationDirectoryPath);
//             ExtractZipFile(finalFile, destinationDirectoryPath);
//             if (File.Exists(finalFile))
//                 File.Delete(finalFile);
//         }
//         finally
//         {
//             if (File.Exists(tempZipPath))
//                 File.Delete(tempZipPath);
//         }
//
//         return true;
//     }
//
//     private static void ExtractZipFile(string path, string destinationDirectory)
//     {
//         using var archive = ZipFile.OpenRead(path);
//
//         // Absolute path of the destination directory
//         var absoluteDestinationPath = Path.GetFullPath(destinationDirectory + Path.AltDirectorySeparatorChar);
//
//         foreach (var entry in archive.Entries)
//         {
//             if (entry.FullName.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase))
//                 continue; // Skip the desktop.ini file
//
//             // Get the full path of the file
//             var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
//
//             // Check for directory traversal attacks
//             if (!destinationPath.StartsWith(absoluteDestinationPath, StringComparison.Ordinal))
//             {
//                 AbortingUpdate("The file path is outside the destination directory. Please contact the developers.");
//                 return;
//             }
//
//             // If the entry is a directory, create it
//             if (entry.FullName.EndsWith(Path.AltDirectorySeparatorChar))
//             {
//                 Directory.CreateDirectory(destinationPath);
//                 continue;
//             }
//
//             // Create directory if it doesn't exist
//             var directoryName = Path.GetDirectoryName(destinationPath);
//             if (!string.IsNullOrEmpty(directoryName))
//                 Directory.CreateDirectory(directoryName);
//
//             // Extract the file
//             entry.ExtractToFile(destinationPath, overwrite: true);
//         }
//     }
//
//     public static void AbortingUpdate(string reason)
//     {
//         new MessageBoxWindow()
//             .SetMessageType(MessageBoxWindow.MessageType.Error)
//             .SetTitleText("Aborting RR Update")
//             .SetInfoText(reason)
//             .Show();
//     }
// }
