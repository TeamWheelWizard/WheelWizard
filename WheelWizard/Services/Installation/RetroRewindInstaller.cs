// using System.IO.Compression;
// using System.Text.RegularExpressions;
// using WheelWizard.Helpers;
// using WheelWizard.Resources.Languages;
// using WheelWizard.Views.Popups.Generic;
//
// namespace WheelWizard.Services.Installation;
//
// public static class RetroRewindInstaller
// {
//     private static readonly string NotInstalledVersion = "Not Installed";
//
//     public static bool IsRetroRewindInstalled() => CurrentRRVersion() != NotInstalledVersion;
//
//     public static string CurrentRRVersion()
//     {
//         var versionFilePath = PathManager.RetroRewindVersionFile;
//         if (!File.Exists(versionFilePath))
//             return NotInstalledVersion;
//
//         var versionText = File.ReadAllText(versionFilePath).Trim();
//         var versionPattern = @"^\d+\.\d+\.\d+$";
//         if (!Regex.IsMatch(versionText, versionPattern))
//             return NotInstalledVersion;
//
//         return versionText;
//     }
//
//     public static async Task<bool> HandleNotInstalled()
//     {
//         var result = await new YesNoWindow()
//             .SetMainText(Phrases.PopupText_RRNotDeterment)
//             .SetExtraText(Phrases.PopupText_DownloadRR)
//             .AwaitAnswer();
//
//         if (!result)
//             return false;
//
//         await InstallRetroRewind();
//         return true;
//     }
//
//     public static async Task<bool> HandleOldVersion()
//     {
//         var result = await new YesNoWindow()
//             .SetMainText(Phrases.PopupText_RRToOld)
//             .SetExtraText(Phrases.PopupText_ReinstallRR)
//             .AwaitAnswer();
//
//         if (!result)
//             return false;
//
//         await InstallRetroRewind();
//         return true;
//     }
//
//     public static async Task InstallRetroRewind()
//     {
//         if (IsRetroRewindInstalled())
//             DeleteExistingRetroRewind();
//
//         if (HasOldRksys())
//         {
//             var rksysQuestion = new YesNoWindow()
//                 .SetMainText(Phrases.PopupText_OldRksysFound)
//                 .SetExtraText(Phrases.PopupText_OldRksysFoundExplained);
//             if (await rksysQuestion.AwaitAnswer())
//                 await BackupOldrksys();
//         }
//         var serverResponse = await HttpClientHelper.GetAsync<string>(Endpoints.RRUrl);
//         if (!serverResponse.Succeeded)
//         {
//             await new MessageBoxWindow()
//                 .SetMessageType(MessageBoxWindow.MessageType.Warning)
//                 .SetTitleText("Could not connect to the server")
//                 .SetInfoText(Phrases.PopupText_CouldNotConnectServer)
//                 .ShowDialog();
//             return;
//         }
//         await DownloadAndExtractRetroRewind(PathManager.RetroRewindTempFile);
//         await RetroRewindUpdater.UpdateRR();
//     }
//
//     public static async Task ReinstallRR()
//     {
//         var result = await new YesNoWindow()
//             .SetMainText(Phrases.PopupText_ReinstallRR)
//             .SetExtraText(Phrases.PopupText_ReinstallQuestion)
//             .AwaitAnswer();
//
//         if (!result)
//             return;
//
//         DeleteExistingRetroRewind();
//         await InstallRetroRewind();
//     }
//
//     private static async Task DownloadAndExtractRetroRewind(string tempZipPath)
//     {
//         var progressWindow = new ProgressWindow(Phrases.PopupText_InstallingRR);
//         progressWindow.SetExtraText(Phrases.PopupText_InstallingRRFirstTime);
//         progressWindow.Show();
//
//         try
//         {
//             await DownloadHelper.DownloadToLocationAsync(Endpoints.RRZipUrl, tempZipPath, progressWindow);
//             progressWindow.SetExtraText(Common.State_Extracting);
//             var extractionPath = PathManager.RiivolutionWhWzFolderPath;
//             ZipFile.ExtractToDirectory(tempZipPath, extractionPath, true);
//         }
//         finally
//         {
//             progressWindow.Close();
//             if (File.Exists(tempZipPath))
//                 File.Delete(tempZipPath);
//         }
//     }
//
//     private static bool HasOldRksys()
//     {
//         return !string.IsNullOrWhiteSpace(GetOldRksys());
//     }
//
//     private static string GetOldRksys()
//     {
//         var rrWfcPaths = new[]
//         {
//             Path.Combine(PathManager.SaveFolderPath),
//             // Also consider the folder with upper-case `Save`
//             Path.Combine(PathManager.RiivolutionWhWzFolderPath, "riivolution", "Save", "RetroWFC"),
//             Path.Combine(PathManager.LoadFolderPath, "Riivolution", "save", "RetroWFC"),
//             Path.Combine(PathManager.LoadFolderPath, "Riivolution", "Save", "RetroWFC"),
//             Path.Combine(PathManager.LoadFolderPath, "riivolution", "save", "RetroWFC"),
//             Path.Combine(PathManager.LoadFolderPath, "riivolution", "Save", "RetroWFC"),
//         };
//
//         foreach (var rrWfc in rrWfcPaths)
//         {
//             if (!Directory.Exists(rrWfc))
//                 continue;
//             var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
//             if (rksysFiles.Length > 0)
//                 return rrWfc;
//         }
//
//         return string.Empty;
//     }
//
//     private static async Task BackupOldrksys()
//     {
//         var rrWfc = Path.Combine(GetOldRksys());
//         if (!Directory.Exists(rrWfc))
//             return;
//         var rksysFiles = Directory.GetFiles(rrWfc, "rksys.dat", SearchOption.AllDirectories);
//         if (rksysFiles.Length == 0)
//             return;
//         var sourceFile = rksysFiles[0];
//         var regionFolder = Path.GetDirectoryName(sourceFile);
//         var regionFolderName = Path.GetFileName(regionFolder);
//         var datFileData = await File.ReadAllBytesAsync(sourceFile);
//         if (regionFolderName == null)
//             return;
//         var destinationFolder = Path.Combine(PathManager.SaveFolderPath, regionFolderName);
//         Directory.CreateDirectory(destinationFolder);
//         var destinationFile = Path.Combine(destinationFolder, "rksys.dat");
//         await File.WriteAllBytesAsync(destinationFile, datFileData);
//     }
//
//     private static void DeleteExistingRetroRewind()
//     {
//         var retroRewindPath = PathManager.RetroRewind6FolderPath;
//         if (Directory.Exists(retroRewindPath))
//             Directory.Delete(retroRewindPath, true);
//     }
// }
