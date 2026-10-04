using System.IO.Abstractions;
using WheelWizard.Models.Mods;
using WheelWizard.Shared.IO;

namespace WheelWizard.Mods;

public interface IModsLaunchService
{
    bool ShouldAskToClearTargetFolder(string targetFolderPath);

    Task<OperationResult> PrepareModsForLaunch(
        string targetFolderPath,
        bool clearTargetFolderWhenNoEnabledMods = false,
        IProgress<ModOperationProgress>? progress = null
    );
}

public sealed class ModsLaunchService(IModManager modManager, IFileSystem fileSystem, IModPaths paths) : IModsLaunchService
{
    private string ModsFolderPath => paths.RootFolderPath;

    public async Task<OperationResult> PrepareModsForLaunch(
        string targetFolderPath,
        bool clearTargetFolderWhenNoEnabledMods = false,
        IProgress<ModOperationProgress>? progress = null
    )
    {
        var mods = modManager.Mods.Where(mod => mod.IsEnabled).ToArray();
        if (mods.Length == 0)
        {
            if (clearTargetFolderWhenNoEnabledMods && ShouldAskToClearTargetFolder(targetFolderPath))
                return fileSystem.DeleteDirectoryIfExists(targetFolderPath);

            return Ok();
        }
        var reversedMods = modManager.Mods.Reverse().ToArray();

        // Build the final file list
        var finalFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // relative path -> source file path

        foreach (var mod in reversedMods)
        {
            if (!mod.IsEnabled)
                continue;

            var modFolder = fileSystem.Path.Combine(ModsFolderPath, mod.Title);
            if (!fileSystem.Directory.Exists(modFolder))
                continue;

            foreach (var file in fileSystem.Directory.GetFiles(modFolder, "*", SearchOption.AllDirectories))
            {
                if (!ShouldCopyFile(mod, file))
                    continue;

                var relativePath = GetLaunchPatchFileName(mod, file);
                // Since higher priority mods overwrite lower ones, we can overwrite entries in the dictionary.
                // Modding archives keep separate filenames so Pulsar can resolve conflicts inside the archives.
                finalFiles[relativePath] = file;
            }
        }

        fileSystem.Directory.CreateDirectory(targetFolderPath);

        progress?.Report(new(ModOperationStage.Installing, 0, TotalFiles: finalFiles.Count));
        return await Task.Run(() => CopyFinalFiles(targetFolderPath, finalFiles, progress));
    }

    public bool ShouldAskToClearTargetFolder(string targetFolderPath) =>
        !modManager.Mods.Any(mod => mod.IsEnabled)
        && fileSystem.Directory.Exists(targetFolderPath)
        && fileSystem.Directory.EnumerateFiles(targetFolderPath).Any();

    private OperationResult CopyFinalFiles(
        string targetFolderPath,
        Dictionary<string, string> finalFiles,
        IProgress<ModOperationProgress>? progress
    )
    {
        try
        {
            var totalFiles = finalFiles.Count;
            var processedFiles = 0;
            if (fileSystem.Directory.Exists(targetFolderPath))
            {
                var files = fileSystem.Directory.GetFiles(targetFolderPath, "*.*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var relativePath = fileSystem.Path.GetFileName(file);
                    if (!finalFiles.ContainsKey(relativePath))
                        fileSystem.File.Delete(file);
                }
            }

            foreach (var kvp in finalFiles)
            {
                var relativePath = kvp.Key;
                var sourceFile = kvp.Value;
                var destinationFile = fileSystem.Path.Combine(targetFolderPath, relativePath);

                processedFiles++;
                progress?.Report(
                    new(ModOperationStage.Installing, (int)(processedFiles / (double)totalFiles * 100), relativePath, totalFiles)
                );

                if (fileSystem.File.Exists(destinationFile))
                {
                    var sourceInfo = fileSystem.FileInfo.New(sourceFile);
                    var destInfo = fileSystem.FileInfo.New(destinationFile);

                    if (sourceInfo.Length == destInfo.Length && sourceInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc)
                        continue;
                }

                fileSystem.File.Copy(sourceFile, destinationFile, true);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to prepare mods for launch: {ex.Message}", Exception = ex };
        }
    }

    private bool ShouldCopyFile(Mod mod, string filePath)
    {
        var modMetadataFile = fileSystem.Path.Combine(ModsFolderPath, mod.Title, $"{mod.Title}.ini");
        if (fileSystem.Path.GetFullPath(filePath).Equals(fileSystem.Path.GetFullPath(modMetadataFile), StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private string GetLaunchPatchFileName(Mod mod, string filePath)
    {
        var fileName = fileSystem.Path.GetFileName(filePath);
        if (!IsModdingArchiveFile(fileName))
            return fileName;

        return $"{mod.Priority}.{StripExistingPriorityPrefix(fileName)}";
    }

    private bool IsModdingArchiveFile(string fileName)
    {
        if (!fileName.EndsWith(".szs", StringComparison.OrdinalIgnoreCase))
            return false;

        var nameWithoutExtension = fileSystem.Path.GetFileNameWithoutExtension(fileName);
        var tagSeparator = nameWithoutExtension.LastIndexOf('.');
        return tagSeparator > 0 && tagSeparator + 1 < nameWithoutExtension.Length;
    }

    private static string StripExistingPriorityPrefix(string fileName)
    {
        var index = 0;
        while (index < fileName.Length && char.IsDigit(fileName[index]))
            index++;

        return index > 0 && index < fileName.Length && fileName[index] == '.' ? fileName[(index + 1)..] : fileName;
    }
}
