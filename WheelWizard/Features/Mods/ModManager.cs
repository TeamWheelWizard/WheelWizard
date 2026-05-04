using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Threading;
using IniParser;
using Serilog;
using WheelWizard.Features.Patches;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Mods;

public interface IModManager : INotifyPropertyChanged
{
    ObservableCollection<Mod> Mods { get; }

    bool IsModInstalled(int modID);

    Task<OperationResult> ReloadAsync();

    Task<OperationResult> SaveModsAsync();

    Task<OperationResult> AddModAsync(Mod mod);

    Task<OperationResult> RemoveModAsync(Mod mod);

    Task<OperationResult> ImportModFilesAsync(string[] filePaths, string modName);

    Task<OperationResult> ToggleAllModsAsync(bool enable);

    OperationResult ValidateModName(string? oldName, string newName);

    OperationResult ValidateRenameModName(string? oldName, string newName);

    Task<OperationResult> RenameModAsync(Mod selectedMod, string newTitle);

    Task<OperationResult> DeleteModAsync(Mod selectedMod);

    OperationResult OpenModFolder(Mod selectedMod);

    Task<OperationResult> InstallModFromFileAsync(string filePath, string givenModName, string author = "-1", int modID = -1);

    Task<OperationResult> DeleteModByIdAsync(int modId);

    Task<OperationResult> MoveModAsync(Mod movedMod, int newIndex);

    Task<OperationResult> SetPriorityAsync(Mod mod, int priority);

    Task<OperationResult> DecreasePriorityAsync(Mod mod);

    Task<OperationResult> IncreasePriorityAsync(Mod mod);

    int GetLowestActivePriority();

    int GetHighestActivePriority();

    Task<OperationResult> MoveModToIndexAsync(Mod mod, int gapIndex);
}

public sealed class ModManager : IModManager
{
    private static readonly char[] _illegalChars = new[] { '.', '/', '~', '\\' };
    private readonly IModInstallationService _modInstallationService;
    private readonly IModPatchConversionService _modPatchConversionService;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private ObservableCollection<Mod> _mods;

    public ObservableCollection<Mod> Mods
    {
        get => _mods;
        private set
        {
            _mods = value;
            OnPropertyChanged(nameof(Mods));
        }
    }

    private bool _isBatchUpdating;

    public ModManager(IModInstallationService modInstallationService, IModPatchConversionService modPatchConversionService)
    {
        _modInstallationService = modInstallationService;
        _modPatchConversionService = modPatchConversionService;
        _mods = [];
    }

    public bool IsModInstalled(int modID)
    {
        return Mods.Any(mod => mod.ModID == modID);
    }

    public async Task<OperationResult> ReloadAsync() => await LoadModsAsync();

    private async Task<OperationResult> LoadModsAsync()
    {
        var newModsResult = await _modInstallationService.LoadModsAsync();
        if (newModsResult.IsFailure)
        {
            return newModsResult;
        }

        foreach (var mod in Mods)
            mod.PropertyChanged -= Mod_PropertyChanged;

        foreach (var mod in newModsResult.Value)
        {
            _modPatchConversionService.RefreshCompatibility(mod);
            mod.PropertyChanged += Mod_PropertyChanged;
        }

        Mods = newModsResult.Value;
        return Ok();
    }

    public async Task<OperationResult> SaveModsAsync()
    {
        var saveResult = await _modInstallationService.SaveModsAsync(Mods);
        if (saveResult.IsFailure)
            return saveResult;

        return Ok();
    }

    public async Task<OperationResult> AddModAsync(Mod mod)
    {
        if (_modInstallationService.ContainsModByTitle(Mods, mod.Title))
            return Fail($"Mod with name '{mod.Title}' already exists.");

        mod.PropertyChanged += Mod_PropertyChanged;
        _modPatchConversionService.RefreshCompatibility(mod);
        Mods.Add(mod);
        SortModsByPriority();
        return await SaveModsAsync();
    }

    public async Task<OperationResult> RemoveModAsync(Mod mod)
    {
        if (!Mods.Contains(mod))
            return Ok();

        mod.PropertyChanged -= Mod_PropertyChanged;
        Mods.Remove(mod);
        OnPropertyChanged(nameof(Mods));
        return await SaveModsAsync();
    }

    private void Mod_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBatchUpdating)
            return;

        if (e.PropertyName == nameof(Mod.Priority))
        {
            SortModsByPriority();
            QueueSaveMods();
            return;
        }

        if (
            e.PropertyName is nameof(Mod.IsEnabled) or nameof(Mod.Title)
            || e.PropertyName == nameof(Mod.Author)
            || e.PropertyName == nameof(Mod.ModID)
            || e.PropertyName == nameof(Mod.HasIncompatibleFiles)
        )
        {
            OnPropertyChanged(nameof(Mods));
            QueueSaveMods();
        }
    }

    private void SortModsByPriority()
    {
        var sortedMods = new ObservableCollection<Mod>(Mods.OrderBy(m => m.Priority));
        Mods = sortedMods;
    }

    public async Task<OperationResult> ImportModFilesAsync(string[] filePaths, string modName)
    {
        var validationResult = ValidateModName(null, modName);
        if (validationResult.IsFailure)
            return validationResult.Error;

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{modName}.zip");
        ProgressWindow? progressWindow = null;

        try
        {
            var totalFiles = filePaths.Length;
            //todo: this is supposed to be backend only, ProgressWindow should not be created here.
            progressWindow = new ProgressWindow(Phrases.Progress_CombiningFiles)
                .SetGoal(Humanizer.ReplaceDynamic(Phrases.Progress_PreparingFilesCount, totalFiles)!)
                .SetExtraText(Common.State_Loading);
            progressWindow.Show();

            var zipResult = await CreateModArchiveAsync(tempZipPath, filePaths, totalFiles, progressWindow);
            if (zipResult.IsFailure)
                return zipResult.Error;

            progressWindow.Close();

            return await InstallModFromFileAsync(tempZipPath, modName, author: "-1", modID: -1);
        }
        finally
        {
            progressWindow?.Close();
            var result = FileHelper.TryDeleteFile(tempZipPath);
            //todo: result should be returned and bubbled up
            if (result.IsFailure)
                Log.Warning(result.Error.Exception, "Failed to delete temporary mod archive: {Message}", result.Error.Message);
        }
    }

    public async Task<OperationResult> ToggleAllModsAsync(bool enable)
    {
        _isBatchUpdating = true;
        try
        {
            foreach (var mod in Mods)
                mod.IsEnabled = enable;
        }
        finally
        {
            _isBatchUpdating = false;
        }

        OnPropertyChanged(nameof(Mods));
        return await SaveModsAsync();
    }

    public OperationResult ValidateModName(string? oldName, string newName)
    {
        newName = newName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return Fail("Mod name cannot be empty.");

        if (_modInstallationService.ContainsModByTitle(Mods, newName))
            return Fail("Mod name already exists.");

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) != -1)
            return Fail("Mod name contains illegal characters.");

        if (newName.Any(x => _illegalChars.Contains(x)))
            return Fail("Mod name contains illegal characters.");

        return Ok();
    }

    public OperationResult ValidateRenameModName(string? oldName, string newName)
    {
        return oldName == newName ? Ok() : ValidateModName(oldName, newName);
    }

    public async Task<OperationResult> RenameModAsync(Mod selectedMod, string newTitle)
    {
        var oldTitle = selectedMod.Title;
        if (oldTitle == newTitle)
            return Ok();

        var validationResult = ValidateRenameModName(oldTitle, newTitle);
        if (validationResult.IsFailure)
            return validationResult.Error;

        var oldDirectoryName = PathManager.GetModDirectoryPath(oldTitle);
        var newDirectoryName = PathManager.GetModDirectoryPath(newTitle);

        if (!Directory.Exists(oldDirectoryName))
            return Fail("The mod folder could not be found.");

        GC.Collect();
        GC.WaitForPendingFinalizers();

        var renameResult = await RenameModDirectoryAsync(selectedMod, oldDirectoryName, newDirectoryName, oldTitle, newTitle);
        if (renameResult.IsFailure)
            return renameResult.Error;

        return await ReloadAsync();
    }

    public async Task<OperationResult> DeleteModAsync(Mod selectedMod)
    {
        var modDirectory = Path.GetFullPath(PathManager.GetModDirectoryPath(selectedMod.Title));

        if (!Directory.Exists(modDirectory))
            return await RemoveModAsync(selectedMod);

        var deleteResult = DeleteModDirectory(modDirectory);
        if (deleteResult.IsFailure)
            return deleteResult.Error;

        return await RemoveModAsync(selectedMod);
    }

    public OperationResult OpenModFolder(Mod selectedMod)
    {
        var modDirectory = PathManager.GetModDirectoryPath(selectedMod.Title);
        if (Directory.Exists(modDirectory))
            return OpenFolder(modDirectory);

        return Fail(Phrases.MessageError_NoModFolder_Extra);
    }

    public async Task<OperationResult> InstallModFromFileAsync(string filePath, string givenModName, string author = "-1", int modID = -1)
    {
        if (_modInstallationService.ContainsModByTitle(Mods, givenModName))
            return Fail($"Mod with name '{givenModName}' already exists.");

        var priority = Mods.Count > 0 ? Mods.Max(m => m.Priority) + 1 : 1;
        var modResult = await _modInstallationService.InstallModFromFileAsync(filePath, givenModName, priority, author, modID);
        if (modResult.IsFailure)
            return modResult.Error;

        return await AddModAsync(modResult.Value);
    }

    public async Task<OperationResult> DeleteModByIdAsync(int modId)
    {
        var modToDelete = Mods.FirstOrDefault(mod => mod.ModID == modId);

        if (modToDelete == null)
            return Fail($"No mod found with ID: {modId}");

        return await DeleteModAsync(modToDelete);
    }

    public async Task<OperationResult> MoveModAsync(Mod movedMod, int newIndex)
    {
        var oldIndex = Mods.IndexOf(movedMod);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Mods.Count)
            return Ok();

        Mods.Move(oldIndex, newIndex);
        _isBatchUpdating = true;
        try
        {
            for (var i = 0; i < Mods.Count; i++)
            {
                Mods[i].Priority = i;
            }
        }
        finally
        {
            _isBatchUpdating = false;
        }

        OnPropertyChanged(nameof(Mods));
        return await SaveModsAsync();
    }

    public async Task<OperationResult> SetPriorityAsync(Mod mod, int priority)
    {
        if (!Mods.Contains(mod))
            return Fail("Cannot find mod.");

        _isBatchUpdating = true;
        try
        {
            mod.Priority = priority;
        }
        finally
        {
            _isBatchUpdating = false;
        }

        SortModsByPriority();
        return await SaveModsAsync();
    }

    private static async Task<OperationResult> CreateModArchiveAsync(
        string tempZipPath,
        string[] filePaths,
        int totalFiles,
        ProgressWindow progressWindow
    )
    {
        try
        {
            await Task.Run(() =>
            {
                using var zipArchive = ZipFile.Open(tempZipPath, ZipArchiveMode.Create);
                var processed = 0;
                foreach (var filePath in filePaths)
                {
                    var entryName = Path.GetFileName(filePath);
                    zipArchive.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
                    processed++;

                    var progress = (int)(processed / (double)totalFiles * 100);
                    Dispatcher.UIThread.Post(() =>
                    {
                        progressWindow.UpdateProgress(progress);
                        progressWindow.SetExtraText($"{Common.State_Installing} {entryName}");
                    });
                }
            });

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to combine files into a mod archive: {ex.Message}", Exception = ex };
        }
    }

    //todo: move to FileHelper (or the DI version if that gets made)
    private static OperationResult OpenFolder(string modDirectory)
    {
        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = modDirectory,
                    UseShellExecute = true,
                    Verb = "open",
                }
            );

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to open mod folder: {ex.Message}", Exception = ex };
        }
    }

    private async Task<OperationResult> RenameModDirectoryAsync(
        Mod selectedMod,
        string oldDirectoryName,
        string newDirectoryName,
        string oldTitle,
        string newTitle
    )
    {
        var renameResult = RenameDirectoryAndMetadataFile(oldDirectoryName, newDirectoryName, oldTitle, newTitle);
        if (renameResult.IsFailure)
            return renameResult;

        _isBatchUpdating = true;
        try
        {
            selectedMod.Title = newTitle;
            _modPatchConversionService.RefreshCompatibility(selectedMod);
        }
        finally
        {
            _isBatchUpdating = false;
        }

        var newIniPath = Path.Combine(newDirectoryName, $"{newTitle}.ini");
        var saveResult = await SaveRenamedModMetadataAsync(selectedMod, newIniPath);
        if (saveResult.IsFailure)
            return saveResult;

        var saveModsResult = await SaveModsAsync();
        if (saveModsResult.IsFailure)
            return saveModsResult;

        OnPropertyChanged(nameof(Mods));

        return FileHelper.DeleteDirectoryIfExists(oldDirectoryName);
    }

    private static OperationResult RenameDirectoryAndMetadataFile(
        string oldDirectoryName,
        string newDirectoryName,
        string oldTitle,
        string newTitle
    )
    {
        try
        {
            Directory.Move(oldDirectoryName, newDirectoryName);
            var newIniPath = Path.Combine(newDirectoryName, $"{newTitle}.ini");
            var updatedOldIniPath = Path.Combine(newDirectoryName, $"{oldTitle}.ini");
            if (File.Exists(updatedOldIniPath))
            {
                File.Move(updatedOldIniPath, newIniPath);
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(newIniPath);
                data["Mod"]["Name"] = newTitle;
                parser.WriteFile(newIniPath, data);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to rename mod directory: {ex.Message}", Exception = ex };
        }
    }

    private static async Task<OperationResult> SaveRenamedModMetadataAsync(Mod selectedMod, string newIniPath)
    {
        try
        {
            await selectedMod.SaveToIniAsync(newIniPath);
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to save renamed mod metadata: {ex.Message}", Exception = ex };
        }
    }

    private static OperationResult DeleteModDirectory(string modDirectory)
    {
        try
        {
            var modsRoot = Path.GetFullPath(PathManager.ModsFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var target = Path.GetFullPath(modDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var relativePath = Path.GetRelativePath(modsRoot, target);

            if (
                relativePath == "."
                || relativePath == ".."
                || relativePath.StartsWith(".." + Path.DirectorySeparatorChar)
                || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar)
                || Path.IsPathRooted(relativePath)
            )
            {
                return Fail("Invalid mod directory.");
            }

            if (!Directory.Exists(target))
                return Fail("Mod directory does not exist.");

            GC.Collect();
            GC.WaitForPendingFinalizers();

            var di = new DirectoryInfo(target);

            foreach (var dir in di.EnumerateDirectories("*", SearchOption.AllDirectories))
                dir.Attributes &= ~FileAttributes.ReadOnly;

            foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes &= ~FileAttributes.ReadOnly;

            di.Attributes &= ~FileAttributes.ReadOnly;

            Directory.Delete(target, true);
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to delete mod directory: {ex.Message}", Exception = ex };
        }
    }

    private void QueueSaveMods()
    {
        _ = EnqueueSaveAsync();
    }

    private async Task EnqueueSaveAsync()
    {
        var semaphoreEntered = false;

        try
        {
            await _saveSemaphore.WaitAsync();
            semaphoreEntered = true;

            await SaveModsAndLogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save mods.");
        }
        finally
        {
            if (semaphoreEntered)
                _saveSemaphore.Release();
        }
    }

    private async Task SaveModsAndLogAsync()
    {
        var saveResult = await SaveModsAsync();
        if (saveResult.IsFailure)
            Log.Error(saveResult.Error.Exception, "Failed to save mods: {Message}", saveResult.Error.Message);
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion

    public async Task<OperationResult> DecreasePriorityAsync(Mod mod)
    {
        if (Mods.IndexOf(mod) == -1)
            return Fail("Cannot find mod to decrease priority.");

        if (mod.Priority == GetLowestActivePriority() || Mods.Count == 1)
            return Fail("Cannot decrease priority of the first mod.");

        // Find mod with next lower priority value
        var modAbove = Mods.Where(m => m.Priority < mod.Priority).OrderByDescending(m => m.Priority).FirstOrDefault();
        if (modAbove == null)
            return Ok();

        _isBatchUpdating = true;
        try
        {
            (modAbove.Priority, mod.Priority) = (mod.Priority, modAbove.Priority);
        }
        finally
        {
            _isBatchUpdating = false;
        }

        SortModsByPriority();
        return await SaveModsAsync();
    }

    public async Task<OperationResult> IncreasePriorityAsync(Mod mod)
    {
        if (Mods.IndexOf(mod) == -1)
            return Fail("Cannot find mod.");

        if (mod.Priority == GetHighestActivePriority() || Mods.Count == 1)
            return Fail("Cannot increase priority of the last mod.");

        // Find mod with next higher priority value
        var modBelow = Mods.Where(m => m.Priority > mod.Priority).OrderBy(m => m.Priority).FirstOrDefault();

        if (modBelow == null)
            return Ok(); // Should not happen but just in case

        // Swap priorities
        _isBatchUpdating = true;
        try
        {
            (modBelow.Priority, mod.Priority) = (mod.Priority, modBelow.Priority);
        }
        finally
        {
            _isBatchUpdating = false;
        }

        SortModsByPriority();
        return await SaveModsAsync();
    }

    public int GetLowestActivePriority() => Mods.Count == 0 ? 0 : Mods.Min(m => m.Priority);

    public int GetHighestActivePriority() => Mods.Count == 0 ? 0 : Mods.Max(m => m.Priority);

    /// <summary>
    /// Moves a mod to a new position in the list using gap-based indexing.
    /// Gap 0 = before first item, gap Count = after last item.
    /// </summary>
    public async Task<OperationResult> MoveModToIndexAsync(Mod mod, int gapIndex)
    {
        var sortedMods = Mods.OrderBy(m => m.Priority).ToList();
        var currentIndex = sortedMods.IndexOf(mod);

        if (currentIndex == -1)
            return Ok();

        // Convert gap index to target index after removal
        int targetIndex;
        if (gapIndex <= currentIndex)
            targetIndex = gapIndex;
        else if (gapIndex > currentIndex + 1)
            targetIndex = gapIndex - 1;
        else
            return Ok(); // No change needed (dropped in same position)

        targetIndex = Math.Clamp(targetIndex, 0, sortedMods.Count - 1);

        if (currentIndex == targetIndex)
            return Ok();

        _isBatchUpdating = true;
        try
        {
            sortedMods.RemoveAt(currentIndex);
            sortedMods.Insert(targetIndex, mod);

            for (var i = 0; i < sortedMods.Count; i++)
                sortedMods[i].Priority = i;
        }
        finally
        {
            _isBatchUpdating = false;
        }

        SortModsByPriority();
        return await SaveModsAsync();
    }
}
