using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using Avalonia.Threading;
using IniParser;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Installation;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services;

public class ModManager : INotifyPropertyChanged
{
    private static readonly Lazy<ModManager> _instance = new(() => new ModManager());
    private static readonly char[] _illegalChars = new[] { '.', '/', '~', '\\' };
    public static ModManager Instance => _instance.Value;

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

    private ModManager()
    {
        _mods = [];
    }

    public bool IsModInstalled(int modID)
    {
        return Mods.Any(mod => mod.ModID == modID);
    }

    public async void ReloadAsync() => await LoadModsAsync();

    private async Task LoadModsAsync()
    {
        try
        {
            // Unsubscribe all the old mods and resubscribe all the new mods
            foreach (var mod in Mods)
            {
                mod.PropertyChanged -= Mod_PropertyChanged;
            }

            var newMods = await ModInstallation.LoadModsAsync();
            foreach (var mod in newMods)
            {
                mod.PropertyChanged += Mod_PropertyChanged;
            }

            Mods = newMods;
        }
        catch (Exception ex)
        {
            ErrorOccurred($"Failed to load mods: {ex.Message}");
        }
    }

    public async void SaveModsAsync()
    {
        try
        {
            await ModInstallation.SaveModsAsync(Mods);
        }
        catch (Exception ex)
        {
            ErrorOccurred($"Failed to save mods: {ex.Message}");
        }
    }

    public void AddMod(Mod mod)
    {
        if (ModInstallation.ModExists(Mods, mod.Title))
            return;

        mod.PropertyChanged += Mod_PropertyChanged;
        Mods.Add(mod);
        SortModsByPriority();
        SaveModsAsync();
    }

    public void RemoveMod(Mod mod)
    {
        if (!Mods.Contains(mod))
            return;

        Mods.Remove(mod);
        SaveModsAsync();
        OnPropertyChanged(nameof(Mods));
    }

    private void Mod_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (_isBatchUpdating)
            return;

        if (e.PropertyName == nameof(Mod.Priority))
        {
            SaveModsAsync();
            SortModsByPriority();
            return;
        }

        if (
            e.PropertyName == nameof(Mod.IsEnabled)
            || e.PropertyName == nameof(Mod.Title)
            || e.PropertyName == nameof(Mod.Author)
            || e.PropertyName == nameof(Mod.ModID)
        )
        {
            SaveModsAsync();
            OnPropertyChanged(e.PropertyName);
        }
    }

    private void SortModsByPriority()
    {
        var sortedMods = new ObservableCollection<Mod>(Mods.OrderBy(m => m.Priority));
        Mods = sortedMods;
    }

    public async void ImportMods()
    {
        var fileType = CustomFilePickerFileType.Mods;
        var selectedFiles = await FilePickerHelper.OpenFilePickerAsync(fileType, allowMultiple: true, title: "Select Mod File");

        if (selectedFiles.Count == 0)
            return;

        await ProcessModFilesAsync(selectedFiles.ToArray());
    }

    private async Task ProcessModFilesAsync(string[] filePaths)
    {
        try
        {
            await CombineFilesIntoSingleModAsync(filePaths);
        }
        catch (Exception ex)
        {
            ErrorOccurred($"Failed to process mod files: {ex.Message}");
        }
    }

    private async Task CombineFilesIntoSingleModAsync(string[] filePaths)
    {
        var modName = await new TextInputWindow()
            .SetMainText("Mod name:")
            .SetPlaceholderText("Enter mod name...")
            .SetValidation(ValidateModName)
            .ShowDialog();
        if (!IsValidName(modName))
            return;

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{modName}.zip");
        ProgressWindow? progressWindow = null;

        try
        {
            var totalFiles = filePaths.Length;
            progressWindow = new ProgressWindow("Combining files")
                .SetGoal($"Preparing {totalFiles} file{(totalFiles == 1 ? "" : "s")}")
                .SetExtraText(Common.State_Loading);
            progressWindow.Show();

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
                        progressWindow?.UpdateProgress(progress);
                        progressWindow?.SetExtraText($"{Common.State_Installing} {entryName}");
                    });
                }
            });

            progressWindow.Close();

            await ModInstallation.InstallModFromFileAsync(tempZipPath, modName, author: "-1", modID: -1);
        }
        catch (Exception ex)
        {
            ErrorOccurred($"Failed to combine and install mod: {ex.Message}");
        }
        finally
        {
            progressWindow?.Close();
            if (File.Exists(tempZipPath))
                File.Delete(tempZipPath);
        }
    }

    public void ToggleAllMods(bool enable)
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

        SaveModsAsync();
        OnPropertyChanged(nameof(Mod.IsEnabled));
    }

    // TODO: Use this validation method when refactoring the ModManager
    public OperationResult ValidateModName(string? oldName, string newName)
    {
        newName = newName?.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return Fail("Mod name cannot be empty.");

        if (ModInstallation.ModExists(Mods, newName))
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

    public async void RenameMod(Mod selectedMod)
    {
        var oldTitle = selectedMod.Title;
        var newTitle = await new TextInputWindow()
            .SetMainText("Mod Name")
            .SetInitialText(oldTitle)
            .SetExtraText($"Changing name from: {oldTitle}")
            .SetPlaceholderText("Enter mod name...")
            .SetValidation(ValidateRenameModName)
            .ShowDialog();

        if (oldTitle == newTitle || newTitle == null)
            return;
        // we don't want to return an error if the name is the same as before, or if the user cancels
        if (!IsValidName(newTitle))
            return;

        var oldDirectoryName = PathManager.GetModDirectoryPath(oldTitle);
        var newDirectoryName = PathManager.GetModDirectoryPath(newTitle);

        // Check if the old directory exists
        if (!Directory.Exists(oldDirectoryName))
            return;

        // var oldIniPath = Path.Combine(oldDirectoryName, $"{oldTitle}.ini");

        GC.Collect();
        GC.WaitForPendingFinalizers();

        // Rename the mod directory first
        try
        {
            Directory.Move(oldDirectoryName, newDirectoryName);
            var newIniPath = Path.Combine(newDirectoryName, $"{newTitle}.ini");
            var updatedOldIniPath = Path.Combine(newDirectoryName, $"{oldTitle}.ini");
            if (File.Exists(updatedOldIniPath))
            {
                File.Move(updatedOldIniPath, newIniPath);
                // Update the mod name inside the .ini file
                var parser = new FileIniDataParser();
                var data = parser.ReadFile(newIniPath);
                data["Mod"]["Name"] = newTitle;
                parser.WriteFile(newIniPath, data);
            }

            // Update the selectedMod's title
            selectedMod.Title = newTitle;
            await selectedMod.SaveToIniAsync(newIniPath);
            SaveModsAsync();
            OnPropertyChanged(nameof(Mods));

            if (Directory.Exists(oldDirectoryName))
                Directory.Delete(oldDirectoryName, true);
        }
        catch (IOException ex)
        {
            ErrorOccurred($"Failed to rename mod directory: {ex.Message}");
        }
        ReloadAsync();
    }

    public async void DeleteMod(Mod selectedMod)
    {
        var areTheySure = await new YesNoWindow()
            .SetMainText(Humanizer.ReplaceDynamic(Phrases.Question_SureDelete_Title, selectedMod.Title)!)
            .AwaitAnswer();
        if (!areTheySure)
            return;

        var modDirectory = Path.GetFullPath(PathManager.GetModDirectoryPath(selectedMod.Title));

        if (!Directory.Exists(modDirectory))
        {
            RemoveMod(selectedMod);
            return;
        }

        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var di = new DirectoryInfo(modDirectory);
            di.Attributes &= ~FileAttributes.ReadOnly;
            foreach (var file in di.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                file.Attributes &= ~FileAttributes.ReadOnly;
            }
            //make sure we are STILL in the mod folder
            var isStillValid = modDirectory.StartsWith(PathManager.ModsFolderPath);
            if (!isStillValid)
            {
                ErrorOccurred("Invalid mod directory.");
                return;
            }
            Directory.Delete(modDirectory, true); // true for recursive deletion
            RemoveMod(selectedMod);
        }
        catch (Exception ex) // Catch a more general exception
        {
            ErrorOccurred($"Failed to delete mod directory: {ex.Message}");
        }
    }

    public void OpenModFolder(Mod selectedMod)
    {
        var modDirectory = PathManager.GetModDirectoryPath(selectedMod.Title);
        if (Directory.Exists(modDirectory))
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = modDirectory,
                    UseShellExecute = true,
                    Verb = "open",
                }
            );
        }
        else
        {
            ErrorOccurred(Phrases.MessageError_NoModFolder_Extra);
        }
    }

    public void DeleteModById(int modId)
    {
        var modToDelete = Mods.FirstOrDefault(mod => mod.ModID == modId);

        if (modToDelete == null)
        {
            ErrorOccurred($"No mod found with ID: {modId}");
            return;
        }

        DeleteMod(modToDelete);
    }

    public void MoveMod(Mod movedMod, int newIndex)
    {
        var oldIndex = Mods.IndexOf(movedMod);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= Mods.Count)
            return;

        Mods.Move(oldIndex, newIndex);
        for (var i = 0; i < Mods.Count; i++)
        {
            Mods[i].Priority = i;
        }

        SaveModsAsync();
        OnPropertyChanged(nameof(Mods));
    }

    private bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorOccurred(Phrases.MessageWarning_ModNameEmpty_Title);
            return false;
        }

        if (!ModInstallation.ModExists(Mods, name))
            return true;

        ErrorOccurred(Humanizer.ReplaceDynamic(Phrases.MessageWarning_InvalidName_Extra_ModNameExists, name));
        return false;
    }

    private void ErrorOccurred(string? errorMessage)
    {
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText("An error occurred")
            .SetInfoText(errorMessage)
            .Show();
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion

    public void DecreasePriority(Mod mod)
    {
        if (Mods.IndexOf(mod) == -1)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Cannot find mod")
                .SetInfoText("Cannot find the mod to decrease priority.")
                .Show();
            return;
        }

        if (mod.Priority == GetLowestActivePriority() || Mods.Count == 1)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Cannot decrease priority")
                .SetInfoText("Cannot decrease priority of the first mod.")
                .Show();
            return;
        }

        // Find mod with next lower priority value
        var modAbove = Mods.Where(m => m.Priority < mod.Priority).OrderByDescending(m => m.Priority).FirstOrDefault();
        if (modAbove == null)
            return;

        (modAbove.Priority, mod.Priority) = (mod.Priority, modAbove.Priority);

        SortModsByPriority();
        SaveModsAsync();
    }

    public void IncreasePriority(Mod mod)
    {
        if (Mods.IndexOf(mod) == -1)
        {
            new MessageBoxWindow().SetMessageType(MessageBoxWindow.MessageType.Error).SetTitleText("Cannot find mod").Show();
            return;
        }

        if (mod.Priority == GetHighestActivePriority() || Mods.Count == 1)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Warning)
                .SetTitleText("Cannot increase priority")
                .SetInfoText("Cannot increase priority of the last mod.")
                .Show();
            return;
        }

        // Find mod with next higher priority value
        var modBelow = Mods.Where(m => m.Priority > mod.Priority).OrderBy(m => m.Priority).FirstOrDefault();

        if (modBelow == null)
            return; // Should not happen but just in case

        // Swap priorities
        (modBelow.Priority, mod.Priority) = (mod.Priority, modBelow.Priority);

        SortModsByPriority();
        SaveModsAsync();
    }

    public int GetLowestActivePriority() => Mods.Min(m => m.Priority);

    public int GetHighestActivePriority() => Mods.Max(m => m.Priority);

    /// <summary>
    /// Moves a mod to a new position in the list using gap-based indexing.
    /// Gap 0 = before first item, gap Count = after last item.
    /// </summary>
    public void MoveModToIndex(Mod mod, int gapIndex)
    {
        var sortedMods = Mods.OrderBy(m => m.Priority).ToList();
        var currentIndex = sortedMods.IndexOf(mod);

        if (currentIndex == -1)
            return;

        // Convert gap index to target index after removal
        int targetIndex;
        if (gapIndex <= currentIndex)
            targetIndex = gapIndex;
        else if (gapIndex > currentIndex + 1)
            targetIndex = gapIndex - 1;
        else
            return; // No change needed (dropped in same position)

        targetIndex = Math.Clamp(targetIndex, 0, sortedMods.Count - 1);

        if (currentIndex == targetIndex)
            return;

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
        SaveModsAsync();
    }
}
