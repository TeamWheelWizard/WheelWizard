using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher.Helpers;

public static class ModsLaunchHelper
{
    public static readonly string MyStuffFolderPath = PathManager.MyStuffFolderPath;
    public static readonly string ModsFolderPath = PathManager.ModsFolderPath;
    public static readonly string[] AcceptedModExtensions = ["*.szs", "*.arc", "*.brstm", "*.brsar", "*.thp"];

    public static async Task PrepareModsForLaunch()
    {
        var mods = ModManager.Instance.Mods.Where(mod => mod.IsEnabled).ToArray();
        if (mods.Length == 0)
        {
            if (Directory.Exists(MyStuffFolderPath) && Directory.EnumerateFiles(MyStuffFolderPath).Any())
            {
                var modsFoundQuestion = new YesNoWindow()
                    .SetButtonText(Common.Action_Delete, Common.Action_Keep)
                    .SetMainText(Phrases.Question_LaunchClearModsFound_Title)
                    .SetExtraText(Phrases.Question_LaunchClearModsFound_Extra);
                if (await modsFoundQuestion.AwaitAnswer())
                    Directory.Delete(MyStuffFolderPath, true);

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
            foreach (var extension in AcceptedModExtensions)
            {
                var files = Directory.GetFiles(modFolder, extension, SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetFileName(file);
                    // Since higher priority mods overwrite lower ones, we can overwrite entries in the dictionary
                    finalFiles[relativePath] = file;
                }
            }
        }

        // Get existing files in MyStuff
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(MyStuffFolderPath))
        {
            var files = Directory.GetFiles(MyStuffFolderPath, "*.*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                var relativePath = Path.GetFileName(file);
                existingFiles.Add(relativePath);
            }
        }
        else
        {
            Directory.CreateDirectory(MyStuffFolderPath);
        }

        var totalFiles = finalFiles.Count;
        var progressWindow = new ProgressWindow(Phrases.Progress_InstallingMods).SetGoal(
            Humanizer.ReplaceDynamic(Phrases.Progress_InstallingModsCount, totalFiles)!
        );
        progressWindow.Show();

        await Task.Run(() =>
        {
            var processedFiles = 0;
            // Delete files in MyStuff that are not in finalFiles
            if (Directory.Exists(MyStuffFolderPath))
            {
                var files = Directory.GetFiles(MyStuffFolderPath, "*.*", SearchOption.TopDirectoryOnly);
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
                var destinationFile = Path.Combine(MyStuffFolderPath, relativePath);

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
}
