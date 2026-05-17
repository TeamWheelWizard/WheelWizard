using System.Collections.ObjectModel;
using Avalonia.Threading;
using SharpCompress.Archives;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Mods;

public interface IModInstallationService
{
    Task<OperationResult<ObservableCollection<Mod>>> LoadModsAsync();

    Task<OperationResult> SaveModsAsync(ObservableCollection<Mod> mods);

    bool ContainsModByTitle(IEnumerable<Mod> mods, string modName);

    Task<OperationResult<Mod>> InstallModFromFileAsync(
        string filePath,
        string givenModName,
        int priority,
        string author = "-1",
        int modID = -1
    );
}

public sealed class ModInstallationService : IModInstallationService
{
    private readonly string _modsFolderPath = PathManager.ModsFolderPath;

    public async Task<OperationResult<ObservableCollection<Mod>>> LoadModsAsync()
    {
        var modsFolderResult = FileHelper.EnsureDirectory(_modsFolderPath);
        if (modsFolderResult.IsFailure)
            return modsFolderResult.Error;

        var iniFilesResult = FileHelper.FindFilesByExtension(_modsFolderPath, "*.ini");
        if (iniFilesResult.IsFailure)
            return iniFilesResult.Error;

        var mods = new ObservableCollection<Mod>();
        foreach (var iniFile in iniFilesResult.Value)
        {
            var modResult = await LoadModFromIniAsync(iniFile);
            if (modResult.IsFailure)
                return modResult.Error;

            if (!string.IsNullOrWhiteSpace(modResult.Value.Title))
                mods.Add(modResult.Value);
        }

        return new ObservableCollection<Mod>(mods.OrderBy(m => m.Priority));
    }

    public async Task<OperationResult> SaveModsAsync(ObservableCollection<Mod> mods)
    {
        foreach (var mod in mods)
        {
            var modDirectory = PathManager.GetModDirectoryPath(mod.Title);
            var directoryResult = FileHelper.EnsureDirectory(modDirectory);
            if (directoryResult.IsFailure)
                return directoryResult.Error;

            var iniFilePath = Path.Combine(modDirectory, $"{mod.Title}.ini");
            var saveResult = await SaveModToIniAsync(mod, iniFilePath);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        return Ok();
    }

    public bool ContainsModByTitle(IEnumerable<Mod> mods, string modName) =>
        mods.Any(mod => mod.Title.Equals(modName, StringComparison.OrdinalIgnoreCase));

    private static async Task<OperationResult<Mod>> LoadModFromIniAsync(string iniFile)
    {
        try
        {
            return await Mod.LoadFromIniAsync(iniFile);
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to load mod metadata '{iniFile}': {ex.Message}", Exception = ex };
        }
    }

    private static async Task<OperationResult> SaveModToIniAsync(Mod mod, string iniFilePath)
    {
        try
        {
            await mod.SaveToIniAsync(iniFilePath);
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to save mod metadata '{iniFilePath}': {ex.Message}", Exception = ex };
        }
    }

    private static OperationResult ExtractModArchive(string file, string destinationDirectory, ProgressWindow progressWindow)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();

        if (!Directory.Exists(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        var archiveResult = OpenArchive(file, extension);
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        try
        {
            using var archive = archiveResult.Value;
            var totalEntries = archive.Entries.Count(entry => !entry.IsDirectory);
            var processedEntries = 0;
            var fullRoot = Path.GetFullPath(destinationDirectory);
            if (!Path.EndsInDirectorySeparator(fullRoot))
                fullRoot += Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                processedEntries++;

                var progress = (int)(processedEntries / (double)totalEntries * 100);
                Dispatcher.UIThread.Post(() =>
                {
                    progressWindow.UpdateProgress(progress);
                });

                var entryKey = entry.Key ?? string.Empty;
                var sanitizedKey = string.Join(
                    Path.DirectorySeparatorChar.ToString(),
                    entryKey
                        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Where(segment => !string.IsNullOrWhiteSpace(segment))
                );

                var entryDestinationPath = Path.Combine(destinationDirectory, sanitizedKey);
                var fullEntry = Path.GetFullPath(entryDestinationPath);

                if (!fullEntry.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    return Fail("Archive entry is outside of the destination directory.");

                var directoryPath = Path.GetDirectoryName(fullEntry);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                    Directory.CreateDirectory(directoryPath);

                using var stream = entry.OpenEntryStream();
                using var fileStream = File.Create(fullEntry);
                stream.CopyTo(fileStream);
            }

            return Ok();
        }
        catch (IOException ex)
        {
            return new IOException("You already have a mod with this name. " + ex.Message, ex);
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to extract archive file: {ex.Message}", Exception = ex };
        }
    }

    private static OperationResult<IArchive> OpenArchive(string filePath, string extension)
    {
        if (extension is not (".zip" or ".7z" or ".rar"))
            return Fail($"Unsupported archive format: {extension}");

        try
        {
            return Ok(ArchiveFactory.Open(filePath));
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to open archive file: {ex.Message}", Exception = ex };
        }
    }

    public async Task<OperationResult<Mod>> InstallModFromFileAsync(
        string filePath,
        string givenModName,
        int priority,
        string author = "-1",
        int modID = -1
    )
    {
        if (!File.Exists(filePath))
            return new FileNotFoundException("File not found.", filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is not (".zip" or ".7z" or ".rar"))
            return Fail($"Unsupported file type: {extension}. Only .zip, .7z, and .rar files are supported.");

        if (string.IsNullOrWhiteSpace(givenModName))
            return Fail("Mod name cannot be empty.");

        ProgressWindow? progressWindow = null;
        try
        {
            progressWindow = new(Phrases.Progress_InstallingMod);
            progressWindow.SetGoal(Common.State_Extracting);
            progressWindow.Show();

            var modDirectory = PathManager.GetModDirectoryPath(givenModName);
            if (!Directory.Exists(modDirectory))
                Directory.CreateDirectory(modDirectory);

            var extractResult = await Task.Run(() => ExtractModArchive(filePath, modDirectory, progressWindow));
            if (extractResult.IsFailure)
                return extractResult.Error;

            var newMod = new Mod
            {
                IsEnabled = true,
                Title = givenModName,
                Author = author,
                ModID = modID,
                Priority = priority,
            };

            var iniFilePath = Path.Combine(modDirectory, $"{givenModName}.ini");
            var saveResult = await SaveModToIniAsync(newMod, iniFilePath);
            if (saveResult.IsFailure)
                return saveResult.Error;

            return newMod;
        }
        finally
        {
            progressWindow?.Close();
        }
    }
}
