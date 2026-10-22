using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomMusic.Audio.Formats;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.CustomMusic.Library;
using WheelWizard.Settings;

namespace WheelWizard.CustomMusic.Playback;

/// <summary>Loads music for the preview player: the original music of a slot, or a song converted for it.</summary>
public interface IMusicPreviewService
{
    /// <summary>Whether the original music of <paramref name="slot"/> is a file that can be played (some menu music is not).</summary>
    bool HasOriginal(MusicSlot slot);

    /// <summary>The music that plays in <paramref name="slot"/> without any custom song: Retro Rewind's file, or the file on the game disc.</summary>
    Task<OperationResult<PreviewClip>> LoadOriginalAsync(MusicSlot slot, CancellationToken cancellationToken = default);

    /// <summary>A BRSTM file on disk, such as a music pack's version of a track.</summary>
    Task<OperationResult<PreviewClip>> LoadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The converted version of a song, exactly as it will be installed.</summary>
    Task<OperationResult<PreviewClip>> LoadCustomAsync(CustomMusicEntry entry, CancellationToken cancellationToken = default);
}

public sealed class MusicPreviewService(
    IFileSystem fileSystem,
    ISettingsManager settings,
    IGameDiscMusicReader discReader,
    ICustomMusicLibrary library,
    ILogger<MusicPreviewService> logger
) : IMusicPreviewService
{
    private const int CachedClips = 6;

    // Reading an original from the disc image takes a moment, so recently played clips are kept.
    private readonly object _cacheGate = new();
    private readonly List<(string Key, PreviewClip Clip)> _cache = [];

    public bool HasOriginal(MusicSlot slot) => slot.RetroRewindFilePath != null || slot.GameFileName != null;

    public Task<OperationResult<PreviewClip>> LoadOriginalAsync(MusicSlot slot, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadOriginal(slot), cancellationToken);

    public Task<OperationResult<PreviewClip>> LoadCustomAsync(CustomMusicEntry entry, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCustom(entry), cancellationToken);

    public Task<OperationResult<PreviewClip>> LoadFileAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                if (!fileSystem.File.Exists(path))
                    return (OperationResult<PreviewClip>)new OperationError { Message = t("custom_music.preview.song_missing") };
                var info = fileSystem.FileInfo.New(path);
                return Decode($"file|{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}", () => fileSystem.File.ReadAllBytes(path));
            },
            cancellationToken
        );

    private OperationResult<PreviewClip> LoadOriginal(MusicSlot slot)
    {
        if (slot.RetroRewindFilePath is { } shipped && fileSystem.File.Exists(shipped))
        {
            var info = fileSystem.FileInfo.New(shipped);
            return Decode($"rr|{shipped}|{info.Length}|{info.LastWriteTimeUtc.Ticks}", () => fileSystem.File.ReadAllBytes(shipped), slot.PlaysAllTracks);
        }

        if (slot.GameFileName is not { } gameFile)
            return new OperationError { Message = t("custom_music.preview.no_original") };

        var gamePath = settings.Get<string>(settings.GAME_LOCATION);
        if (string.IsNullOrWhiteSpace(gamePath) || !fileSystem.File.Exists(gamePath))
            return new OperationError { Message = t("custom_music.preview.needs_disc") };

        var disc = fileSystem.FileInfo.New(gamePath);
        return Decode(
            $"disc|{gamePath}|{disc.Length}|{disc.LastWriteTimeUtc.Ticks}|{gameFile}",
            () =>
            {
                var bytes = discReader.ReadStreamFile(gamePath, gameFile);
                return bytes.IsFailure ? throw new InvalidDataException(bytes.Error.Message) : bytes.Value;
            },
            slot.PlaysAllTracks
        );
    }

    private OperationResult<PreviewClip> LoadCustom(CustomMusicEntry entry)
    {
        var path = fileSystem.Path.Combine(library.GetStorageFolder(entry.StorageId), entry.NormalFile);
        if (!fileSystem.File.Exists(path))
            return new OperationError { Message = t("custom_music.preview.song_missing") };
        // Custom songs are re-converted when changed, so the storage id identifies the file.
        return Decode($"custom|{entry.StorageId}|{entry.NormalFile}", () => fileSystem.File.ReadAllBytes(path));
    }

    private OperationResult<PreviewClip> Decode(string key, Func<byte[]> read, bool allTracks = false)
    {
        lock (_cacheGate)
        {
            var hit = _cache.FindIndex(item => item.Key == key);
            if (hit >= 0)
            {
                var item = _cache[hit];
                _cache.RemoveAt(hit);
                _cache.Add(item); // most recently used last
                return item.Clip;
            }
        }

        try
        {
            var clip = PreviewClip.FromBrstm(read(), allTracks);
            lock (_cacheGate)
            {
                _cache.Add((key, clip));
                if (_cache.Count > CachedClips)
                    _cache.RemoveAt(0);
            }

            return clip;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or IndexOutOfRangeException or ArgumentException)
        {
            logger.LogWarning(exception, "Could not load music for the preview.");
            return new OperationError { Message = t("custom_music.preview.failed", new { message = exception.Message }), Exception = exception };
        }
    }
}
