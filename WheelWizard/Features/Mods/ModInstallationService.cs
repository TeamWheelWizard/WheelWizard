using System.Collections.ObjectModel;
using System.IO.Abstractions;
using SharpCompress.Archives;
using WheelWizard.Models.Mods;
using WheelWizard.Shared.IO;

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
        int modID = -1,
        IProgress<ModOperationProgress>? progress = null
    );
}

public sealed class ModInstallationService(IFileSystem fileSystem, IModPaths paths) : IModInstallationService
{
    private string ModsFolderPath => paths.RootFolderPath;

    public async Task<OperationResult<ObservableCollection<Mod>>> LoadModsAsync()
    {
        var modsFolderResult = fileSystem.EnsureDirectory(ModsFolderPath);
        if (modsFolderResult.IsFailure)
            return modsFolderResult.Error;

        var iniFilesResult = fileSystem.FindFilesByExtension(ModsFolderPath, "*.ini");
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
            var modDirectory = paths.GetModDirectoryPath(mod.Title);
            var directoryResult = fileSystem.EnsureDirectory(modDirectory);
            if (directoryResult.IsFailure)
                return directoryResult.Error;

            var iniFilePath = fileSystem.Path.Combine(modDirectory, $"{mod.Title}.ini");
            var saveResult = await SaveModToIniAsync(mod, iniFilePath);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        return Ok();
    }

    public bool ContainsModByTitle(IEnumerable<Mod> mods, string modName) =>
        mods.Any(mod => mod.Title.Equals(modName, StringComparison.OrdinalIgnoreCase));

    private async Task<OperationResult<Mod>> LoadModFromIniAsync(string iniFile)
    {
        try
        {
            return ModMetadata.Parse(await fileSystem.File.ReadAllTextAsync(iniFile));
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to load mod metadata '{iniFile}': {ex.Message}", Exception = ex };
        }
    }

    private async Task<OperationResult> SaveModToIniAsync(Mod mod, string iniFilePath)
    {
        try
        {
            await fileSystem.File.WriteAllTextAsync(iniFilePath, ModMetadata.Serialize(mod));
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to save mod metadata '{iniFilePath}': {ex.Message}", Exception = ex };
        }
    }

    private OperationResult ExtractModArchive(string file, string destinationDirectory, IProgress<ModOperationProgress>? progress)
    {
        var extension = fileSystem.Path.GetExtension(file).ToLowerInvariant();

        if (!fileSystem.Directory.Exists(destinationDirectory))
            fileSystem.Directory.CreateDirectory(destinationDirectory);

        using var archiveStream = fileSystem.File.OpenRead(file);
        var archiveResult = OpenArchive(archiveStream, extension);
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        try
        {
            using var archive = archiveResult.Value;
            var totalEntries = archive.Entries.Count(entry => !entry.IsDirectory);
            var processedEntries = 0;

            foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
            {
                processedEntries++;

                progress?.Report(
                    new(ModOperationStage.Extracting, (int)(processedEntries / (double)totalEntries * 100), entry.Key, totalEntries)
                );

                var entryKey = entry.Key ?? string.Empty;
                if (!PathSafety.TryGetPathWithinDirectory(destinationDirectory, entryKey, out var fullEntry))
                    return Fail("Archive entry is outside of the destination directory.");

                var directoryPath = fileSystem.Path.GetDirectoryName(fullEntry);
                if (!string.IsNullOrEmpty(directoryPath) && !fileSystem.Directory.Exists(directoryPath))
                    fileSystem.Directory.CreateDirectory(directoryPath);

                using var stream = entry.OpenEntryStream();
                using var fileStream = fileSystem.File.Create(fullEntry);
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

    private OperationResult<IArchive> OpenArchive(Stream stream, string extension)
    {
        if (extension is not (".zip" or ".7z" or ".rar"))
            return Fail($"Unsupported archive format: {extension}");

        try
        {
            return Ok(ArchiveFactory.OpenArchive(stream));
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
        int modID = -1,
        IProgress<ModOperationProgress>? progress = null
    )
    {
        if (!fileSystem.File.Exists(filePath))
            return new FileNotFoundException("File not found.", filePath);

        var extension = fileSystem.Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is not (".zip" or ".7z" or ".rar"))
            return Fail($"Unsupported file type: {extension}. Only .zip, .7z, and .rar files are supported.");

        if (string.IsNullOrWhiteSpace(givenModName))
            return Fail("Mod name cannot be empty.");

        try
        {
            progress?.Report(new(ModOperationStage.Extracting, 0));

            var modDirectory = paths.GetModDirectoryPath(givenModName);
            if (!fileSystem.Directory.Exists(modDirectory))
                fileSystem.Directory.CreateDirectory(modDirectory);

            var extractResult = await Task.Run(() => ExtractModArchive(filePath, modDirectory, progress));
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

            var iniFilePath = fileSystem.Path.Combine(modDirectory, $"{givenModName}.ini");
            var saveResult = await SaveModToIniAsync(newMod, iniFilePath);
            if (saveResult.IsFailure)
                return saveResult.Error;

            return newMod;
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to install mod: {ex.Message}", Exception = ex };
        }
    }
}
