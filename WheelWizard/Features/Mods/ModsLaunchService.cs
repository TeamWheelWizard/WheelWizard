using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Mods;

public interface IModsLaunchService
{
    bool ShouldAskToClearTargetFolder(string targetFolderPath);

    Task<OperationResult> PrepareModsForLaunch(string targetFolderPath, bool clearTargetFolderWhenNoEnabledMods = false);
}

public sealed class ModsLaunchService(IModManager modManager) : IModsLaunchService
{
    private static readonly string ModsFolderPath = PathManager.ModsFolderPath;

    public async Task<OperationResult> PrepareModsForLaunch(string targetFolderPath, bool clearTargetFolderWhenNoEnabledMods = false)
    {
        var mods = modManager.Mods.Where(mod => mod.IsEnabled).ToArray();
        if (mods.Length == 0)
        {
            if (clearTargetFolderWhenNoEnabledMods && ShouldAskToClearTargetFolder(targetFolderPath))
                return FileHelper.DeleteDirectoryIfExists(targetFolderPath);

            return Ok();
        }
        var reversedMods = modManager.Mods.Reverse().ToArray();

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
                if (!ShouldCopyFile(mod, file))
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

        var copyResult = await Task.Run(() => CopyFinalFiles(targetFolderPath, finalFiles, progressWindow));

        progressWindow.Close();
        return copyResult;
    }

    public bool ShouldAskToClearTargetFolder(string targetFolderPath) =>
        !modManager.Mods.Any(mod => mod.IsEnabled)
        && Directory.Exists(targetFolderPath)
        && Directory.EnumerateFiles(targetFolderPath).Any();

    private static OperationResult CopyFinalFiles(
        string targetFolderPath,
        Dictionary<string, string> finalFiles,
        ProgressWindow progressWindow
    )
    {
        try
        {
            var totalFiles = finalFiles.Count;
            var processedFiles = 0;
            if (Directory.Exists(targetFolderPath))
            {
                var files = Directory.GetFiles(targetFolderPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var relativePath = Path.GetFileName(file);
                    if (!finalFiles.ContainsKey(relativePath))
                        File.Delete(file);
                }
            }

            foreach (var kvp in finalFiles)
            {
                var relativePath = kvp.Key;
                var sourceFile = kvp.Value;
                var destinationFile = Path.Combine(targetFolderPath, relativePath);

                processedFiles++;
                var progress = (int)(processedFiles / (double)totalFiles * 100);
                Dispatcher.UIThread.Post(() =>
                {
                    progressWindow.UpdateProgress(progress);
                    progressWindow.SetExtraText($"{Common.State_Installing} {relativePath}");
                });

                if (File.Exists(destinationFile))
                {
                    var sourceInfo = new FileInfo(sourceFile);
                    var destInfo = new FileInfo(destinationFile);

                    if (sourceInfo.Length == destInfo.Length && sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc)
                        continue;
                }

                File.Copy(sourceFile, destinationFile, true);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to prepare mods for launch: {ex.Message}", Exception = ex };
        }
    }

    private static bool ShouldCopyFile(Mod mod, string filePath)
    {
        var modMetadataFile = Path.Combine(ModsFolderPath, mod.Title, $"{mod.Title}.ini");
        if (Path.GetFullPath(filePath).Equals(Path.GetFullPath(modMetadataFile), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
