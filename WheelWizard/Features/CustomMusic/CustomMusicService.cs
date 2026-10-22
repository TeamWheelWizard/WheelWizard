using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;

namespace WheelWizard.CustomMusic;

/// <summary>The custom music workflow: list slots, convert songs, and save them into the game.</summary>
public interface ICustomMusicService
{
    Task<MusicCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default);

    CustomMusicLibraryData LoadLibrary();

    /// <summary>Persists view preferences (sort order, final lap style, volume adjustment) without touching saved music.</summary>
    void SavePreferences(MusicSortMode sortMode, FinalLapStyle finalLapStyle, double volumeOffsetDb);

    /// <summary>Converts <paramref name="sourcePath"/> into BRSTM(s) for <paramref name="slot"/>. Nothing is installed until <see cref="SaveAsync"/>.</summary>
    Task<OperationResult<CustomMusicEntry>> ConvertAsync(
        MusicSlot slot,
        string sourcePath,
        FinalLapStyle finalLapStyle,
        double volumeOffsetDb = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Installs <paramref name="entries"/> into the game (via the "Custom Music" patch) and remembers them.</summary>
    Task<OperationResult> SaveAsync(IReadOnlyCollection<CustomMusicEntry> entries, IReadOnlyCollection<MusicSlot> catalog, CancellationToken cancellationToken = default);

    /// <summary>Deletes converted files of conversions that were never saved.</summary>
    void DiscardUnsaved();

    string? GetCoverPath(CustomMusicEntry entry);

    CustomMusicModState GetModState();

    Task<OperationResult> EnableModAsync();

    IReadOnlyList<MusicConflict> FindConflicts();

    /// <inheritdoc cref="ICustomMusicInstaller.FindPackMusic"/>
    IReadOnlyDictionary<string, PackMusic> FindPackMusic();

    /// <summary>
    /// Extracts the songs in a music pack (.zip) into the library, so they can be matched to tracks and converted.
    /// Returns the extracted files.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> ExtractPackAsync(string zipPath, CancellationToken cancellationToken = default);
}

public sealed class CustomMusicService(
    IMusicCatalogService catalog,
    ICustomMusicLibrary library,
    IMusicReferenceService references,
    ICustomMusicInstaller installer,
    IFileSystem fileSystem,
    ILogger<CustomMusicService> logger
) : ICustomMusicService
{
    // library.json is read-modify-written by preference changes and saves, which can overlap.
    private readonly object _libraryLock = new();

    public Task<MusicCatalog> LoadCatalogAsync(CancellationToken cancellationToken = default) => catalog.LoadAsync(cancellationToken);

    public CustomMusicLibraryData LoadLibrary()
    {
        lock (_libraryLock)
        {
            var data = library.Load();
            // Leftovers of conversions that were never saved (e.g. the app closed with unsaved changes).
            library.DeleteUnused(data.Entries);
            return data;
        }
    }

    public void SavePreferences(MusicSortMode sortMode, FinalLapStyle finalLapStyle, double volumeOffsetDb)
    {
        lock (_libraryLock)
        {
            var data = library.Load() with { SortMode = sortMode, FinalLapStyle = finalLapStyle, VolumeOffsetDb = volumeOffsetDb };
            var result = library.Save(data);
            if (result.IsFailure)
                logger.LogWarning("Could not save custom music preferences: {Message}", result.Error.Message);
        }
    }

    public async Task<OperationResult<CustomMusicEntry>> ConvertAsync(
        MusicSlot slot,
        string sourcePath,
        FinalLapStyle finalLapStyle,
        double volumeOffsetDb = 0,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        string? storageFolder = null;
        try
        {
            if (!fileSystem.File.Exists(sourcePath))
                return Fail(t("custom_music.error.file_missing"));

            progress?.Report(0.02);
            var reference = await references.GetReferenceAsync(slot, cancellationToken);

            var audio = await AudioFileDecoder.DecodeAsync(fileSystem, sourcePath, cancellationToken);
            progress?.Report(0.25);

            var options = new ConversionOptions
            {
                Loop = reference.Loops ?? slot.Loops,
                CreateFinalLap = slot.HasFinalLap,
                FinalLapStyle = finalLapStyle,
                // Match the loudness of the original file and carry its volume byte, so both play at the same level.
                TargetLufs = reference.Lufs,
                VolumeOffsetDb = volumeOffsetDb,
                PulsarVolume = reference.PulsarVolume,
            };

            var conversionProgress = progress == null ? null : new Progress<double>(value => progress.Report(0.25 + value * 0.73));
            var result = await Task.Run(() => MusicConverter.Convert(audio, options, conversionProgress, cancellationToken), cancellationToken);

            var (storageId, folder) = library.CreateStorage();
            storageFolder = folder;
            await fileSystem.File.WriteAllBytesAsync(fileSystem.Path.Combine(folder, "normal.brstm"), result.Normal, cancellationToken);
            string? finalFile = null;
            if (result.FinalLap != null && !ReferenceEquals(result.FinalLap, result.Normal))
            {
                finalFile = "final.brstm";
                await fileSystem.File.WriteAllBytesAsync(fileSystem.Path.Combine(folder, finalFile), result.FinalLap, cancellationToken);
            }

            string? coverFile = null;
            if (audio.Metadata.Cover is { } cover)
            {
                coverFile = "cover" + cover.FileExtension;
                await fileSystem.File.WriteAllBytesAsync(fileSystem.Path.Combine(folder, coverFile), cover.Data, cancellationToken);
            }

            progress?.Report(1.0);
            return new CustomMusicEntry
            {
                Key = slot.Key,
                StorageId = storageId,
                SourcePath = sourcePath,
                Title = audio.Metadata.Title,
                Artist = audio.Metadata.Artist,
                Album = audio.Metadata.Album,
                CoverFile = coverFile,
                AddedUtc = DateTime.UtcNow,
                SourceLufs = result.Report.SourceLufs,
                TargetLufs = result.Report.TargetLufs,
                VolumeOffsetDb = volumeOffsetDb,
                ReferenceSource = reference.Source,
                ReferenceNote = reference.Source == MusicReferenceSource.Estimate ? reference.Problem : null,
                ReferenceLufs = reference.Lufs,
                ReferenceVolume = reference.PulsarVolume,
                OutputLufs = result.Report.OutputLufs,
                PulsarVolume = result.Report.PulsarVolume,
                FinalLapStyle = finalLapStyle,
                DurationSeconds = result.Report.Duration.TotalSeconds,
                Loop = result.Report.Loop,
                LoopStartSeconds = result.Report.LoopStartSample / (double)options.SampleRate,
                LoopEndSeconds = result.Report.Loop == LoopKind.None ? 0 : result.Report.SampleCount / (double)options.SampleRate,
                NormalFile = "normal.brstm",
                FinalLapFile = finalFile,
            };
        }
        catch (OperationCanceledException)
        {
            DeleteQuietly(storageFolder);
            return Fail(t("custom_music.error.cancelled"));
        }
        catch (NotSupportedException exception)
        {
            DeleteQuietly(storageFolder);
            return Fail(exception.Message);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            DeleteQuietly(storageFolder);
            logger.LogWarning(exception, "Converting {Source} for {Slot} failed.", sourcePath, slot.Key);
            return new OperationError { Message = exception.Message, Exception = exception };
        }
        catch (Exception exception)
        {
            // Third-party decoders throw a variety of exceptions on damaged files.
            DeleteQuietly(storageFolder);
            logger.LogError(exception, "Unexpected error converting {Source}.", sourcePath);
            return new OperationError { Message = t("custom_music.error.unreadable"), Exception = exception };
        }
    }

    public async Task<OperationResult> SaveAsync(
        IReadOnlyCollection<CustomMusicEntry> entries,
        IReadOnlyCollection<MusicSlot> catalog,
        CancellationToken cancellationToken = default
    )
    {
        var slots = catalog.ToDictionary(slot => slot.Key);
        var install = new List<InstalledMusic>();
        foreach (var entry in entries)
        {
            // Entries for tracks that are no longer in Retro Rewind stay remembered but are not installed.
            if (!slots.TryGetValue(entry.Key, out var slot))
                continue;

            var folder = library.GetStorageFolder(entry.StorageId);
            var normal = fileSystem.Path.Combine(folder, entry.NormalFile);
            var final = entry.FinalLapFile == null ? null : fileSystem.Path.Combine(folder, entry.FinalLapFile);
            install.Add(new InstalledMusic(slot, normal, final));
        }

        var applyResult = await installer.ApplyAsync(install, cancellationToken);
        if (applyResult.IsFailure)
            return applyResult;

        lock (_libraryLock)
        {
            var current = library.Load();
            var saveResult = library.Save(current with { Entries = entries.ToList() });
            if (saveResult.IsFailure)
                return saveResult;

            library.DeleteUnused(entries);
        }

        return Ok();
    }

    public void DiscardUnsaved()
    {
        lock (_libraryLock)
            library.DeleteUnused(library.Load().Entries);
    }

    public string? GetCoverPath(CustomMusicEntry entry)
    {
        if (entry.CoverFile == null)
            return null;
        var path = fileSystem.Path.Combine(library.GetStorageFolder(entry.StorageId), entry.CoverFile);
        return fileSystem.File.Exists(path) ? path : null;
    }

    public CustomMusicModState GetModState() => installer.GetState();

    public Task<OperationResult> EnableModAsync() => installer.EnableAsync();

    public IReadOnlyList<MusicConflict> FindConflicts() => installer.FindConflicts();

    public IReadOnlyDictionary<string, PackMusic> FindPackMusic() => installer.FindPackMusic();

    public Task<OperationResult<IReadOnlyList<string>>> ExtractPackAsync(string zipPath, CancellationToken cancellationToken = default) =>
        Task.Run(() => library.ExtractPack(zipPath, cancellationToken), cancellationToken);

    private void DeleteQuietly(string? folder)
    {
        if (folder == null)
            return;
        try
        {
            if (fileSystem.Directory.Exists(folder))
                fileSystem.Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not clean up {Folder}.", folder);
        }
    }
}
