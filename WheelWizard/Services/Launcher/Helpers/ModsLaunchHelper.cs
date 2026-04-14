using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher.Helpers;

public static class ModsLaunchHelper
{
    public static readonly string ModsFolderPath = PathManager.ModsFolderPath;

    //todo: move this to like an actual static place somewhere that makes more sense.
    public static readonly string[] AcceptedMyStuffExtensions =
    [
        ".bcp",
        ".szs",
        ".bdof",
        ".bfg",
        ".blight",
        ".bmg",
        ".bmm",
        ".brctr",
        ".breff",
        ".breft",
        ".brfnt",
        ".brlan",
        ".brlyt",
        ".brres",
        ".brsar",
        ".brstm",
        ".bsp",
        ".bti",
        ".chr0",
        ".clr0",
        ".krm",
        ".mdl0",
        ".pat0",
        ".rkc",
        ".rkg",
        ".scn0",
        ".shp0",
        ".srt0",
        ".tex0",
        ".thp",
        ".tpl",
        ".u8",
        ".yaz0",
        ".ast",
        ".bdl",
        ".bmd",
        ".bco",
        ".bol",
        ".dat",
        ".rarc",
        ".ct-def",
        ".le-def",
        ".lex",
        ".lfl",
        ".lpar",
        ".lta",
        ".tplx",
        ".wbz",
        ".wlz",
        ".wu8",
        ".ybz",
        ".ylz",
    ];

    public static async Task PrepareModsForLaunch(string targetFolderPath, string inactiveFolderPath, ModStorageSystem storageSystem)
    {
        ClearInactiveFolder(inactiveFolderPath);

        var mods = ModManager.Instance.Mods.Where(mod => mod.IsEnabled).ToArray();
        if (mods.Length == 0)
        {
            if (Directory.Exists(targetFolderPath) && Directory.EnumerateFiles(targetFolderPath).Any())
            {
                var modsFoundQuestion = new YesNoWindow()
                    .SetButtonText(Common.Action_Delete, Common.Action_Keep)
                    .SetMainText(Phrases.Question_LaunchClearModsFound_Title)
                    .SetExtraText(ModStorageSystemHelper.GetClearFolderPrompt(storageSystem));
                if (await modsFoundQuestion.AwaitAnswer())
                    Directory.Delete(targetFolderPath, true);

                return;
            }
        }
        var reversedMods = ModManager.Instance.Mods.Reverse().ToArray();

        // Build the final file list
        var finalFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // relative path -> source file path

        foreach (var mod in reversedMods)
        {
            if (!mod.IsEnabled)
                continue;

            var modFolder = Path.Combine(ModsFolderPath, mod.Title);
            if (!Directory.Exists(modFolder))
                continue;

            foreach (var file in Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories))
            {
                if (!ShouldCopyFile(mod, file, storageSystem))
                    continue;

                var relativePath = Path.GetFileName(file);
                // Since higher priority mods overwrite lower ones, we can overwrite entries in the dictionary
                finalFiles[relativePath] = file;
            }
        }

        Directory.CreateDirectory(targetFolderPath);

        var totalFiles = finalFiles.Count;
        var progressWindow = new ProgressWindow(Phrases.Progress_InstallingMods).SetGoal(
            Humanizer.ReplaceDynamic(Phrases.Progress_InstallingModsCount, totalFiles)!
        );
        progressWindow.Show();

        await Task.Run(() =>
        {
            var processedFiles = 0;
            // Delete files in the active loose-mod folder that are not in finalFiles
            if (Directory.Exists(targetFolderPath))
            {
                var files = Directory.GetFiles(targetFolderPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var relativePath = Path.GetFileName(file);
                    if (!finalFiles.ContainsKey(relativePath))
                    {
                        File.Delete(file);
                    }
                }
            }

            foreach (var kvp in finalFiles)
            {
                var relativePath = kvp.Key;
                var sourceFile = kvp.Value;
                var destinationFile = Path.Combine(targetFolderPath, relativePath);

                processedFiles++;
                var progress = (int)((processedFiles) / (double)totalFiles * 100);
                Dispatcher.UIThread.Post(() =>
                {
                    progressWindow.UpdateProgress(progress);
                    progressWindow.SetExtraText($"{Common.State_Installing} {relativePath}");
                });

                // Check if the destination file exists and is identical
                if (File.Exists(destinationFile))
                {
                    var sourceInfo = new FileInfo(sourceFile);
                    var destInfo = new FileInfo(destinationFile);

                    if (sourceInfo.Length == destInfo.Length && sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc)
                    {
                        // Files are identical, skip copying
                        continue;
                    }
                    else
                    {
                        // Files are different, copy over
                        File.Copy(sourceFile, destinationFile, true);
                    }
                }
                else
                {
                    // Destination file doesn't exist, copy it
                    File.Copy(sourceFile, destinationFile, true);
                }
            }
        });

        progressWindow.Close();
    }

    private static void ClearInactiveFolder(string inactiveFolderPath)
    {
        if (!Directory.Exists(inactiveFolderPath))
            return;

        Directory.Delete(inactiveFolderPath, true);
    }

    private static bool ShouldCopyFile(Mod mod, string filePath, ModStorageSystem storageSystem)
    {
        var modMetadataFile = Path.Combine(ModsFolderPath, mod.Title, $"{mod.Title}.ini");
        if (Path.GetFullPath(filePath).Equals(Path.GetFullPath(modMetadataFile), StringComparison.OrdinalIgnoreCase))
            return false;

        if (storageSystem == ModStorageSystem.Patches)
            return true;

        var extension = Path.GetExtension(filePath);
        return AcceptedMyStuffExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
