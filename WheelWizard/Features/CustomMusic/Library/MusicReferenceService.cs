using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomMusic.Audio;
using WheelWizard.CustomMusic.Audio.Dsp;
using WheelWizard.CustomMusic.Audio.Formats;
using WheelWizard.CustomMusic.Catalog;
using WheelWizard.Settings;

namespace WheelWizard.CustomMusic.Library;

/// <summary>
/// The music being replaced: how loud the file itself is (<paramref name="Lufs"/>, before any volume byte), whether it
/// loops, and the volume byte Retro Rewind applies to it (0 = none). A replacement is matched to the loudness and
/// gets the same byte, so both play at the same level. <paramref name="Problem"/> says why the original could not
/// be measured when <paramref name="Source"/> is <see cref="MusicReferenceSource.Estimate"/>.
/// </summary>
public sealed record MusicReference(double Lufs, bool? Loops, MusicReferenceSource Source, byte PulsarVolume = 0, string? Problem = null);

public interface IMusicReferenceService
{
    /// <summary>
    /// Measures the music that currently plays in <paramref name="slot"/>: Retro Rewind's own BRSTM when it
    /// ships one, otherwise the original file on the player's game disc. Results are cached on disk.
    /// </summary>
    Task<MusicReference> GetReferenceAsync(MusicSlot slot, CancellationToken cancellationToken = default);
}

public sealed class MusicReferenceService(
    IFileSystem fileSystem,
    ICustomMusicLibrary library,
    IGameDiscMusicReader discReader,
    ISettingsManager settings,
    ILogger<MusicReferenceService> logger
) : IMusicReferenceService
{
    private sealed record CachedReference(double Lufs, bool Loops, MusicReferenceSource Source, byte PulsarVolume = 0);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, CachedReference>? _cache;

    private string CachePath => fileSystem.Path.Combine(library.RootFolderPath, "reference-loudness.json");

    public async Task<MusicReference> GetReferenceAsync(MusicSlot slot, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var cache = LoadCache();
            string? problem = null;

            if (slot.RetroRewindFilePath is { } shipped && fileSystem.File.Exists(shipped))
            {
                var key = Identity("rr", shipped, shipped + Tracks(slot));
                if (cache.TryGetValue(key, out var cached))
                    return From(cached);

                var measured = await Task.Run(() => Measure(fileSystem.File.ReadAllBytes(shipped), slot.PlaysAllTracks), cancellationToken);
                if (measured is { } result)
                    return Remember(cache, key, result, MusicReferenceSource.RetroRewind);
                problem = t("custom_music.reference_problem.unreadable_file", new { file = fileSystem.Path.GetFileName(shipped) });
            }

            var gamePath = settings.Get<string>(settings.GAME_LOCATION);
            if (slot.GameFileName is { } gameFile)
            {
                if (string.IsNullOrWhiteSpace(gamePath) || !fileSystem.File.Exists(gamePath))
                {
                    problem ??= t("custom_music.disc.not_set");
                }
                else
                {
                    var key = Identity("disc", gamePath, gameFile + Tracks(slot));
                    if (cache.TryGetValue(key, out var cached))
                        return From(cached);

                    var bytes = await Task.Run(() => discReader.ReadStreamFile(gamePath, gameFile), cancellationToken);
                    if (bytes.IsFailure)
                    {
                        logger.LogWarning("Could not read {File} from the game disc: {Message}", gameFile, bytes.Error.Message);
                        problem ??= bytes.Error.Message;
                    }
                    else if (await Task.Run(() => Measure(bytes.Value, slot.PlaysAllTracks), cancellationToken) is { } result)
                    {
                        return Remember(cache, key, result, MusicReferenceSource.GameDisc);
                    }
                    else
                    {
                        problem ??= t("custom_music.reference_problem.unreadable_file", new { file = gameFile });
                    }
                }
            }

            return new(EstimateFrom(cache), null, MusicReferenceSource.Estimate, 0, problem);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// For music we cannot measure (menu music is sequenced, not streamed), use the typical loudness of the
    /// music we did measure, so menus end up as loud as the courses.
    /// </summary>
    private static double EstimateFrom(Dictionary<string, CachedReference> cache)
    {
        var measured = cache
            .Where(pair => pair.Value.Source != MusicReferenceSource.Estimate)
            .Select(pair => pair.Value.Lufs)
            .Order()
            .ToList();
        return measured.Count >= 3 ? measured[measured.Count / 2] : MusicLoudness.DefaultTargetLufs;
    }

    /// <summary>Prefix of every cache key. Changing how music is measured means changing it, so stale results are ignored.</summary>
    private const string CacheVersion = "v1|";

    /// <summary>The same file measures differently depending on which of its tracks are heard, so that is part of the key.</summary>
    private static string Tracks(MusicSlot slot) => slot.PlaysAllTracks ? "|all-tracks" : "|first-track";

    private static MusicReference From(CachedReference cached) => new(cached.Lufs, cached.Loops, cached.Source, cached.PulsarVolume);

    private (double Lufs, bool Loops, byte PulsarVolume)? Measure(byte[] brstm, bool allTracks)
    {
        try
        {
            var info = BrstmReader.ReadInfo(brstm);
            // Measure what is heard: a file can carry extra tracks the game keeps silent (or plays on top).
            var audio = BrstmReader.DecodeAsPlayed(brstm, allTracks);
            var loudness = LoudnessMeter.Measure(audio);
            if (loudness.IsSilent)
                return null;
            // The file's own loudness. Pulsar multiplies the sound's volume by header byte 0x3F / 127 while it
            // plays; instead of folding that into the number, replacements get the same byte (see MusicReference).
            return (loudness.IntegratedLufs, info.Looping, info.PulsarVolume);
        }
        catch (Exception exception) when (exception is InvalidDataException or IndexOutOfRangeException or ArgumentException)
        {
            logger.LogDebug(exception, "Could not measure a reference BRSTM.");
            return null;
        }
    }

    private MusicReference Remember(
        Dictionary<string, CachedReference> cache,
        string key,
        (double Lufs, bool Loops, byte PulsarVolume) measured,
        MusicReferenceSource source
    )
    {
        cache[key] = new CachedReference(measured.Lufs, measured.Loops, source, measured.PulsarVolume);
        try
        {
            fileSystem.Directory.CreateDirectory(library.RootFolderPath);
            fileSystem.File.WriteAllText(CachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not store the reference loudness cache.");
        }

        return new(measured.Lufs, measured.Loops, source, measured.PulsarVolume);
    }

    private Dictionary<string, CachedReference> LoadCache()
    {
        if (_cache != null)
            return _cache;

        try
        {
            if (fileSystem.File.Exists(CachePath))
            {
                // Results measured another way (another CacheVersion) are dropped and measured again.
                _cache = JsonSerializer
                    .Deserialize<Dictionary<string, CachedReference>>(fileSystem.File.ReadAllText(CachePath))
                    ?.Where(pair => pair.Key.StartsWith(CacheVersion, StringComparison.Ordinal))
                    .ToDictionary();
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            logger.LogDebug(exception, "Ignoring an unreadable reference loudness cache.");
        }

        return _cache ??= new();
    }

    /// <summary>A cache key that changes whenever the source file changes (new Retro Rewind version, other game dump).</summary>
    private string Identity(string kind, string containerPath, string item)
    {
        var info = fileSystem.FileInfo.New(containerPath);
        return $"{CacheVersion}{kind}|{containerPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{item}".ToLowerInvariant();
    }
}
