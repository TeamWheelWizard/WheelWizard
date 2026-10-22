using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;

namespace WheelWizard.CustomMusic.Library;

/// <summary>A converted song and the slot it replaces.</summary>
public sealed record InstalledMusic(MusicSlot Slot, string NormalPath, string? FinalLapPath);

public enum CustomMusicModState
{
    /// <summary>No custom music has been saved yet.</summary>
    NotCreated,
    Enabled,

    /// <summary>The player switched the "Custom Music" entry off on the Patches page.</summary>
    Disabled,
}

/// <summary>A file of ours that another enabled patch with higher priority also provides (theirs wins).</summary>
public sealed record MusicConflict(string FileName, string ModTitle);

/// <summary>A music file another enabled patch (a music pack) puts in the game.</summary>
public sealed record PackMusic(string ModTitle, string Path);

public interface ICustomMusicInstaller
{
    CustomMusicModState GetState();

    /// <summary>
    /// Writes every converted song into the "Custom Music" patch (creating and enabling it when needed) and removes
    /// files of songs that were reset. Wheel Wizard copies enabled patches into Retro Rewind's Patches folder at launch.
    /// </summary>
    Task<OperationResult> ApplyAsync(IReadOnlyCollection<InstalledMusic> music, CancellationToken cancellationToken = default);

    Task<OperationResult> EnableAsync();

    /// <summary>Other enabled patches that would override some of our music files.</summary>
    IReadOnlyList<MusicConflict> FindConflicts();

    /// <summary>
    /// Music files that other enabled patches (music packs) put in the game, by file name (case-insensitive),
    /// resolved the way they are copied at launch: when two packs have the same file, the higher priority wins.
    /// </summary>
    IReadOnlyDictionary<string, PackMusic> FindPackMusic();
}

public sealed class CustomMusicInstaller(IModManager mods, IModPaths modPaths, IFileSystem fileSystem, ILogger<CustomMusicInstaller> logger)
    : ICustomMusicInstaller
{
    public const string ModTitle = "Custom Music";
    private const string ModAuthor = "Wheel Wizard";

    public CustomMusicModState GetState()
    {
        var mod = FindMod();
        return mod == null ? CustomMusicModState.NotCreated
            : mod.IsEnabled ? CustomMusicModState.Enabled
            : CustomMusicModState.Disabled;
    }

    public async Task<OperationResult> ApplyAsync(IReadOnlyCollection<InstalledMusic> music, CancellationToken cancellationToken = default)
    {
        var mod = await FindOrLoadModAsync();

        if (music.Count == 0)
        {
            // Nothing customised any more: remove the patch entirely instead of leaving an empty one behind.
            return mod == null ? Ok() : await mods.DeleteModAsync(mod);
        }

        if (mod == null)
        {
            mod = new Mod
            {
                Title = ModTitle,
                Author = ModAuthor,
                ModID = -1,
                IsEnabled = true,
                // Lowest number = highest priority: per-track choices made here should beat music packs.
                Priority = mods.Mods.Count == 0 ? 0 : mods.GetLowestActivePriority() - 1,
            };
            var addResult = await mods.AddModAsync(mod);
            if (addResult.IsFailure)
                return addResult;
        }
        else if (!mod.IsEnabled)
        {
            mod.IsEnabled = true;
            var saveResult = await mods.SaveModsAsync();
            if (saveResult.IsFailure)
                return saveResult;
        }

        var folder = modPaths.GetModDirectoryPath(mod.Title);
        return await Task.Run(() => WriteFiles(folder, mod.Title, music, cancellationToken), cancellationToken);
    }

    public async Task<OperationResult> EnableAsync()
    {
        var mod = await FindOrLoadModAsync();
        if (mod == null || mod.IsEnabled)
            return Ok();
        mod.IsEnabled = true;
        return await mods.SaveModsAsync();
    }

    public IReadOnlyList<MusicConflict> FindConflicts()
    {
        var mod = FindMod();
        if (mod == null || !mod.IsEnabled)
            return [];

        var ourFiles = ListBrstmFiles(modPaths.GetModDirectoryPath(mod.Title)).Select(fileSystem.Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ourFiles.Count == 0)
            return [];

        var conflicts = new List<MusicConflict>();
        // Lower priority numbers are copied last and win (see ModsLaunchService).
        foreach (var other in mods.Mods.Where(m => m.IsEnabled && !ReferenceEquals(m, mod) && m.Priority < mod.Priority))
        {
            foreach (var file in ListBrstmFiles(modPaths.GetModDirectoryPath(other.Title)))
            {
                var name = fileSystem.Path.GetFileName(file);
                if (ourFiles.Contains(name))
                    conflicts.Add(new MusicConflict(name, other.Title));
            }
        }

        return conflicts;
    }

    public IReadOnlyDictionary<string, PackMusic> FindPackMusic()
    {
        var ours = FindMod();
        var music = new Dictionary<string, PackMusic>(StringComparer.OrdinalIgnoreCase);
        // The list runs from highest to lowest priority; going backwards lets higher priorities overwrite.
        foreach (var mod in mods.Mods.Reverse().Where(m => m.IsEnabled && !ReferenceEquals(m, ours)))
        {
            foreach (var file in ListBrstmFiles(modPaths.GetModDirectoryPath(mod.Title)))
                music[fileSystem.Path.GetFileName(file)] = new PackMusic(mod.Title, file);
        }

        return music;
    }

    private OperationResult WriteFiles(string folder, string title, IReadOnlyCollection<InstalledMusic> music, CancellationToken cancellationToken)
    {
        try
        {
            fileSystem.Directory.CreateDirectory(folder);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in music)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var name in item.Slot.FileNames)
                    Copy(item.NormalPath, name);
                foreach (var name in item.Slot.FinalLapFileNames)
                    Copy(item.FinalLapPath ?? item.NormalPath, name);
            }

            // Remove music for slots that were reset (anything we did not just write).
            foreach (var file in fileSystem.Directory.EnumerateFiles(folder))
            {
                var name = fileSystem.Path.GetFileName(file);
                if (name.Equals($"{title}.ini", StringComparison.OrdinalIgnoreCase) || written.Contains(name))
                    continue;
                fileSystem.File.Delete(file);
            }

            return Ok();

            void Copy(string source, string name)
            {
                // The Patches system matches by file name only, so the music must sit directly in the patch folder.
                if (name.IndexOfAny(fileSystem.Path.GetInvalidFileNameChars()) >= 0)
                    return;
                fileSystem.File.Copy(source, fileSystem.Path.Combine(folder, name), overwrite: true);
                written.Add(name);
            }
        }
        catch (OperationCanceledException)
        {
            return Fail("Saving custom music was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Writing custom music files failed.");
            return new OperationError { Message = $"Could not write the custom music files: {exception.Message}", Exception = exception };
        }
    }

    private IEnumerable<string> ListBrstmFiles(string folder) =>
        fileSystem.Directory.Exists(folder)
            ? fileSystem.Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(file => file.EndsWith(".brstm", StringComparison.OrdinalIgnoreCase))
            : [];

    private Mod? FindMod() =>
        mods.Mods.FirstOrDefault(mod => mod.Title.Equals(ModTitle, StringComparison.OrdinalIgnoreCase))
        ?? mods.Mods.FirstOrDefault(mod => mod.Author == ModAuthor && mod.ModID == -1 && mod.Title.Contains("Music", StringComparison.OrdinalIgnoreCase));

    private async Task<Mod?> FindOrLoadModAsync()
    {
        var mod = FindMod();
        if (mod != null)
            return mod;

        // The Patches page loads the list lazily; make sure we are not looking at an empty, unloaded list.
        var reload = await mods.ReloadAsync();
        if (reload.IsFailure)
            logger.LogWarning("Could not reload patches while saving custom music: {Message}", reload.Error.Message);
        return FindMod();
    }
}
