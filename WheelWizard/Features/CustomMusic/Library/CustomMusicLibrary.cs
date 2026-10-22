using System.IO.Abstractions;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using WheelWizard.ApplicationData;
using WheelWizard.CustomMusic.Audio;

namespace WheelWizard.CustomMusic.Library;

public enum MusicSortMode
{
    /// <summary>Cup order, the way tracks appear in game.</summary>
    Default,
    Alphabetical,

    /// <summary>Customised music first.</summary>
    CustomFirst,

    /// <summary>Grouped by where the music plays: menus and lobby, races, battle arenas, then jingles.</summary>
    ByType,
}

public enum MusicReferenceSource
{
    RetroRewind,
    GameDisc,
    Estimate,
}

/// <summary>A song the player assigned to a music slot, and the converted BRSTM files made from it.</summary>
public sealed record CustomMusicEntry
{
    public required string Key { get; init; }

    /// <summary>Folder (inside the library) holding the converted files and cover art.</summary>
    public required string StorageId { get; init; }

    public required string SourcePath { get; init; }
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }

    /// <summary>Cover art file name inside the storage folder, if the song had any.</summary>
    public string? CoverFile { get; init; }

    public DateTime AddedUtc { get; init; } = DateTime.UtcNow;

    public double SourceLufs { get; init; }
    public double TargetLufs { get; init; }
    public double VolumeOffsetDb { get; init; }

    /// <summary>Loudness of the original music file (null when it was not recorded).</summary>
    public double? ReferenceLufs { get; init; }

    /// <summary>Volume byte of the original music file (0 = none).</summary>
    public byte ReferenceVolume { get; init; }

    /// <summary>Loudness of the converted file (null when it was not recorded).</summary>
    public double? OutputLufs { get; init; }
    public MusicReferenceSource ReferenceSource { get; init; }

    /// <summary>Why the original music could not be measured (only set when <see cref="ReferenceSource"/> is an estimate).</summary>
    public string? ReferenceNote { get; init; }

    /// <summary>The volume byte written to the BRSTM header (0 = none). It starts at the original's byte and can be higher.</summary>
    public byte PulsarVolume { get; init; }

    public FinalLapStyle FinalLapStyle { get; init; }
    public double DurationSeconds { get; init; }
    public LoopKind Loop { get; init; }

    /// <summary>Where the loop starts and ends in the converted song, in seconds (0 when it does not loop).</summary>
    public double LoopStartSeconds { get; init; }

    public double LoopEndSeconds { get; init; }

    public string NormalFile { get; init; } = "normal.brstm";
    public string? FinalLapFile { get; init; }

    [JsonIgnore]
    public string DisplayTitle => !string.IsNullOrWhiteSpace(Title) ? Title! : PathText.FileNameWithoutExtension(SourcePath);

    /// <summary>
    /// How much louder (positive) or quieter the song plays in game than the original, in dB, counting the volume
    /// bytes of both. Null when either loudness is unknown.
    /// </summary>
    [JsonIgnore]
    public double? LoudnessVersusOriginalDb =>
        OutputLufs is { } output && ReferenceLufs is { } reference && double.IsFinite(output) && double.IsFinite(reference)
            ? output + MusicLoudness.VolumeByteDb(PulsarVolume) - (reference + MusicLoudness.VolumeByteDb(ReferenceVolume))
            : null;
}

public sealed record CustomMusicLibraryData
{
    public int Version { get; init; } = 1;
    public MusicSortMode SortMode { get; init; } = MusicSortMode.Default;
    public FinalLapStyle FinalLapStyle { get; init; } = FinalLapStyle.Faster;

    /// <summary>Volume adjustment (dB) applied to songs added from now on.</summary>
    public double VolumeOffsetDb { get; init; }

    public List<CustomMusicEntry> Entries { get; init; } = [];
}

public interface ICustomMusicLibrary
{
    string RootFolderPath { get; }

    CustomMusicLibraryData Load();

    OperationResult Save(CustomMusicLibraryData data);

    /// <summary>A fresh, empty folder for a new conversion.</summary>
    (string StorageId, string FolderPath) CreateStorage();

    string GetStorageFolder(string storageId);

    /// <summary>
    /// Removes converted files and extracted music packs that none of <paramref name="entriesInUse"/> refers to.
    /// </summary>
    void DeleteUnused(IEnumerable<CustomMusicEntry> entriesInUse);

    /// <summary>Extracts the audio files of a music pack (.zip) into the library and returns their paths.</summary>
    OperationResult<IReadOnlyList<string>> ExtractPack(string zipPath, CancellationToken cancellationToken = default);
}

public sealed class CustomMusicLibrary(IFileSystem fileSystem, IApplicationDataLocation applicationData, ILogger<CustomMusicLibrary> logger)
    : ICustomMusicLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string RootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "CustomMusic");
    private string LibraryFilePath => fileSystem.Path.Combine(RootFolderPath, "library.json");
    private string StorageRootPath => fileSystem.Path.Combine(RootFolderPath, "Converted");
    private string PacksRootPath => fileSystem.Path.Combine(RootFolderPath, "Packs");

    /// <summary>Largest music pack accepted, so a wrong archive cannot fill the disk.</summary>
    private const long MaxPackBytes = 4L * 1024 * 1024 * 1024;

    public CustomMusicLibraryData Load()
    {
        try
        {
            if (!fileSystem.File.Exists(LibraryFilePath))
                return new();

            var data = JsonSerializer.Deserialize<CustomMusicLibraryData>(fileSystem.File.ReadAllText(LibraryFilePath), JsonOptions) ?? new();
            // Drop entries that were edited into something unsafe, or whose converted files went missing.
            var valid = data.Entries.Where(entry =>
                IsValidStorageId(entry.StorageId)
                && IsPlainFileName(entry.NormalFile)
                && (entry.FinalLapFile == null || IsPlainFileName(entry.FinalLapFile))
                && (entry.CoverFile == null || IsPlainFileName(entry.CoverFile))
                && fileSystem.File.Exists(fileSystem.Path.Combine(GetStorageFolder(entry.StorageId), entry.NormalFile))
            );
            return data with { Entries = valid.ToList() };
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(exception, "Could not read the custom music library; starting with an empty one.");
            return new();
        }
    }

    public OperationResult Save(CustomMusicLibraryData data)
    {
        try
        {
            fileSystem.Directory.CreateDirectory(RootFolderPath);
            var temporary = LibraryFilePath + ".tmp";
            fileSystem.File.WriteAllText(temporary, JsonSerializer.Serialize(data, JsonOptions));
            fileSystem.File.Move(temporary, LibraryFilePath, overwrite: true);
            return Ok();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new OperationError { Message = $"Could not save the custom music library: {exception.Message}", Exception = exception };
        }
    }

    public (string StorageId, string FolderPath) CreateStorage()
    {
        var id = Guid.NewGuid().ToString("N");
        var folder = GetStorageFolder(id);
        fileSystem.Directory.CreateDirectory(folder);
        return (id, folder);
    }

    public string GetStorageFolder(string storageId)
    {
        // Storage ids are our own GUIDs; refuse anything that could escape the library folder.
        if (!IsValidStorageId(storageId))
            throw new ArgumentException("Invalid storage id.", nameof(storageId));
        return fileSystem.Path.Combine(StorageRootPath, storageId);
    }

    private static bool IsValidStorageId(string? storageId) => !string.IsNullOrEmpty(storageId) && storageId.All(char.IsAsciiLetterOrDigit);

    private static bool IsPlainFileName(string? name) => name != null && PathText.IsValidFileName(name);

    public void DeleteUnused(IEnumerable<CustomMusicEntry> entriesInUse)
    {
        var entries = entriesInUse.ToList();
        var storage = entries.Select(entry => entry.StorageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeleteFoldersExcept(StorageRootPath, folder => storage.Contains(fileSystem.Path.GetFileName(folder)));

        // A pack folder stays while any song in use was taken from it.
        var sources = entries.Select(entry => fileSystem.Path.GetFullPath(entry.SourcePath)).ToList();
        DeleteFoldersExcept(
            PacksRootPath,
            folder =>
            {
                var prefix = fileSystem.Path.GetFullPath(folder) + fileSystem.Path.DirectorySeparatorChar;
                return sources.Any(source => source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            }
        );
    }

    public OperationResult<IReadOnlyList<string>> ExtractPack(string zipPath, CancellationToken cancellationToken = default)
    {
        var folder = fileSystem.Path.Combine(PacksRootPath, $"{PathText.FileNameWithoutExtension(zipPath)}-{Guid.NewGuid():N}"[..^24]);
        try
        {
            using var stream = fileSystem.File.OpenRead(zipPath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var extracted = new List<string>();
            long total = 0;
            fileSystem.Directory.CreateDirectory(folder);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Packs mirror the game's folders (sound/strm/...); only the file name matters for matching, and
                // flattening it also keeps entries like "../x" from leaving the folder.
                var name = PathText.FileName(entry.FullName);
                if (!AudioFileDecoder.IsSupportedExtension(name) || !PathText.IsValidFileName(name) || entry.Length > AudioFileDecoder.MaxFileSize)
                    continue;
                total += entry.Length;
                if (total > MaxPackBytes)
                    break;

                var target = fileSystem.Path.Combine(folder, name);
                if (fileSystem.File.Exists(target))
                    continue; // the same song in two folders of the pack
                using (var input = entry.Open())
                using (var output = fileSystem.File.Create(target))
                    input.CopyTo(output);
                extracted.Add(target);
            }

            if (extracted.Count == 0)
                fileSystem.Directory.Delete(folder, recursive: true);
            return extracted;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            DeleteQuietly(folder);
            logger.LogWarning(exception, "Could not extract the music pack {Pack}.", zipPath);
            return new OperationError { Message = t("custom_music.error.pack", new { message = exception.Message }), Exception = exception };
        }
    }

    private void DeleteFoldersExcept(string root, Func<string, bool> keep)
    {
        if (!fileSystem.Directory.Exists(root))
            return;
        foreach (var folder in fileSystem.Directory.EnumerateDirectories(root))
        {
            if (!keep(folder))
                DeleteQuietly(folder);
        }
    }

    private void DeleteQuietly(string folder)
    {
        try
        {
            if (fileSystem.Directory.Exists(folder))
                fileSystem.Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not remove unused custom music files in {Folder}.", folder);
        }
    }
}
