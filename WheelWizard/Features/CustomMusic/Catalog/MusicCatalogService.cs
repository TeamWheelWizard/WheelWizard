using System.IO.Abstractions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions;

namespace WheelWizard.CustomMusic.Catalog;

public interface IMusicCatalogService
{
    /// <summary>Builds the list of replaceable music: the installed Retro Rewind tracks plus the original game's music.</summary>
    Task<MusicCatalog> LoadAsync(CancellationToken cancellationToken = default);
}

public sealed class MusicCatalogService(IFileSystem fileSystem, ICustomDistributionPaths paths, ILogger<MusicCatalogService> logger)
    : IMusicCatalogService
{
    private static readonly string[] ConfigFiles = ["ConfigRT.pul", "ConfigCT.pul", "ConfigBT.pul"];

    public Task<MusicCatalog> LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() => Load(cancellationToken), cancellationToken);

    private MusicCatalog Load(CancellationToken cancellationToken)
    {
        var gameSlots = GameMusicCatalog.Build();
        var state = RetroRewindCatalogState.NotInstalled;
        var retroRewindSlots = new List<MusicSlot>();
        var shippedMusic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (fileSystem.Directory.Exists(paths.RetroRewindFolderPath))
            {
                shippedMusic = IndexRetroRewindMusic();
                var gameFiles = gameSlots.Where(slot => slot.GameFileName != null).ToDictionary(slot => slot.Key, slot => slot.GameFileName!);
                retroRewindSlots = BuildRetroRewindSlots(shippedMusic, gameFiles, cancellationToken);
                state = retroRewindSlots.Count > 0 ? RetroRewindCatalogState.Loaded : RetroRewindCatalogState.Unreadable;
            }
        }
        // A damaged config can also make the binary readers run off the end of the data.
        catch (Exception exception)
            when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            logger.LogWarning(exception, "Could not read the Retro Rewind track list for custom music.");
            state = RetroRewindCatalogState.Unreadable;
        }

        // How many Retro Rewind tracks fall back to each original song (they have no music of their own).
        var slotUsage = retroRewindSlots
            .Where(slot => slot.RetroRewindFilePath == null && slot.CourseId is { } id && GameMusicCatalog.KeyForCourseId(id) != null)
            .GroupBy(slot => GameMusicCatalog.KeyForCourseId(slot.CourseId!.Value)!)
            .ToDictionary(group => group.Key, group => group.Count());

        var finalGameSlots = gameSlots
            .Select(slot =>
            {
                var updated = slot;
                if (slotUsage.TryGetValue(slot.Key, out var users))
                {
                    var usage = $"Also plays on {users} Retro Rewind {(users == 1 ? "track" : "tracks")}";
                    updated = updated with { Detail = updated.Detail == null ? usage : $"{updated.Detail}. {usage}" };
                }

                // Retro Rewind may ship its own versions of menu music (title_bg.brstm, ...).
                var shipped = slot.FileNames.Select(name => shippedMusic.GetValueOrDefault(name)).FirstOrDefault(path => path != null);
                return shipped == null ? updated : updated with { RetroRewindFilePath = shipped };
            })
            .ToList();

        return new MusicCatalog(retroRewindSlots.Concat(finalGameSlots).ToList(), state);
    }

    private List<MusicSlot> BuildRetroRewindSlots(
        Dictionary<string, string> shippedMusic,
        Dictionary<string, string> gameFiles,
        CancellationToken cancellationToken
    )
    {
        var binaries = fileSystem.Path.Combine(paths.RetroRewindFolderPath, "Binaries");
        var configs = ConfigFiles.Select(name => fileSystem.Path.Combine(binaries, name)).Where(fileSystem.File.Exists).ToList();
        if (configs.Count == 0)
        {
            var single = fileSystem.Path.Combine(binaries, "Config.pul");
            if (fileSystem.File.Exists(single))
                configs.Add(single);
        }

        var slots = new List<MusicSlot>();
        var globalOffset = 0;
        foreach (var configPath in configs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var config = PulsarConfigReader.Read(fileSystem.File.ReadAllBytes(configPath));
            var isBattle = fileSystem.Path.GetFileName(configPath).Equals("ConfigBT.pul", StringComparison.OrdinalIgnoreCase);
            var configKey = fileSystem.Path.GetFileNameWithoutExtension(configPath).ToLowerInvariant();

            foreach (var track in config.Tracks)
            {
                var globalIndex = globalOffset + track.LocalIndex;
                var slot = BuildTrackSlot(track, config, globalIndex, isBattle, configKey, shippedMusic, gameFiles);
                if (slot != null)
                    slots.Add(slot);
            }

            globalOffset += config.CupCount * 4;
        }

        // Keys must be unique; duplicated creator names would otherwise collide.
        var unique = slots.GroupBy(slot => slot.Key).Select(group => group.First()).ToList();

        // Tracks that share a name get their file name appended so they can be told apart.
        var duplicated = unique.GroupBy(slot => slot.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unique
            .Select(slot => duplicated.Contains(slot.Name) ? slot with { Name = $"{slot.Name} ({StripSuffix(slot.FileNames[0])})" } : slot)
            .ToList();
    }

    private MusicSlot? BuildTrackSlot(
        PulsarTrack track,
        PulsarConfig config,
        int globalIndex,
        bool isBattle,
        string configKey,
        Dictionary<string, string> shippedMusic,
        Dictionary<string, string> gameFiles
    )
    {
        // Retro Rewind looks for strm/<name>_n.brstm, trying the creator file name, the track name and
        // finally the track index (see BRSTMExpansion.cpp in Retro Rewind's Pulsar). Our file only has to
        // win the first lookup, so it is written under the first usable name.
        var candidates = new[] { track.FileName, track.Name, globalIndex.ToString() }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var baseName = candidates.FirstOrDefault(IsUsableFileName);
        if (baseName == null)
            return null;

        var shipped = candidates.Select(name => shippedMusic.GetValueOrDefault($"{name}_n.brstm")).FirstOrDefault(path => path != null);

        var normalNames = new List<string> { $"{baseName}_n.brstm" };
        var finalNames = new List<string> { $"{baseName}_final.brstm" };
        foreach (var variant in track.Variants.Where(v => v.FileName != null && IsUsableFileName(v.FileName)))
        {
            normalNames.Add($"{variant.FileName}_n.brstm");
            finalNames.Add($"{variant.FileName}_final.brstm");
        }

        // Some tracks switch to new music at checkpoints (name_n2, name_n3, ...); replace those parts too.
        for (var part = 2; part <= 9; part++)
        {
            if (candidates.Any(name => shippedMusic.ContainsKey($"{name}_n{part}.brstm")))
                normalNames.Add($"{baseName}_n{part}.brstm");
        }

        var name = track.Name ?? track.FileName ?? $"Track {globalIndex}";
        var cupName = track.CupIndex < config.CupNames.Count ? config.CupNames[track.CupIndex] : $"Cup {track.CupIndex + 1}";
        var musicCourse = GameMusicCatalog.CourseName(track.MusicSlot);
        var detail = shipped != null
            ? "Retro Rewind music"
            : musicCourse != null
                ? $"Uses {musicCourse} music"
                : null;

        // The file name tells apart tracks whose names look alike.
        detail = detail == null ? baseName : $"{detail} · {baseName}";

        string? gameFile = null;
        if (shipped == null && GameMusicCatalog.KeyForCourseId(track.MusicSlot) is { } slotKey)
            gameFile = gameFiles.GetValueOrDefault(slotKey);

        return new MusicSlot
        {
            Key = "rr:" + (track.FileName ?? $"{configKey}:{track.LocalIndex}").ToLowerInvariant(),
            Name = name,
            Category = isBattle ? MusicCategory.RetroRewindArena : MusicCategory.RetroRewindTrack,
            Group = cupName,
            Order = GameMusicCatalog.RetroRewindOrderBase + globalIndex,
            FileNames = normalNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            FinalLapFileNames = finalNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            RetroRewindFilePath = shipped,
            GameFileName = gameFile,
            CourseId = track.MusicSlot,
            Detail = detail,
            SearchText = string.Join(' ', candidates.Append(cupName)),
        };
    }

    private static string StripSuffix(string fileName) => fileName.EndsWith("_n.brstm", StringComparison.OrdinalIgnoreCase) ? fileName[..^8] : fileName;

    // The music is written as <name>_n.brstm, so the name must be a valid file name everywhere.
    private static bool IsUsableFileName(string name) => name.Length <= 48 && PathText.IsValidFileName(name);

    /// <summary>Every BRSTM Retro Rewind maps onto /sound/strm, indexed by file name.</summary>
    private Dictionary<string, string> IndexRetroRewindMusic()
    {
        var folders = new List<string>
        {
            fileSystem.Path.Combine(paths.RetroRewindFolderPath, "strm"),
            fileSystem.Path.Combine(paths.RetroRewindFolderPath, "CTBRSTM"),
        };

        try
        {
            if (fileSystem.File.Exists(paths.XmlFilePath))
            {
                var document = XDocument.Parse(fileSystem.File.ReadAllText(paths.XmlFilePath));
                foreach (var folder in document.Descendants().Where(element => element.Name.LocalName == "folder"))
                {
                    var disc = folder.Attribute("disc")?.Value.Trim('/') ?? string.Empty;
                    var external = folder.Attribute("external")?.Value;
                    if (external == null || !disc.Equals("sound/strm", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var relative = external.Replace("{$pack}", "RetroRewind6").TrimStart('/', '\\').Replace('/', fileSystem.Path.DirectorySeparatorChar);
                    folders.Add(fileSystem.Path.Combine(paths.RootFolderPath, relative));
                }
            }
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or IOException)
        {
            logger.LogDebug(exception, "Could not read the Retro Rewind XML while looking for music folders.");
        }

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase).Where(fileSystem.Directory.Exists))
        {
            // Match the extension ourselves: wildcard patterns are case sensitive on Linux.
            foreach (var file in fileSystem.Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly))
            {
                if (file.EndsWith(".brstm", StringComparison.OrdinalIgnoreCase))
                    index.TryAdd(fileSystem.Path.GetFileName(file), file);
            }
        }

        return index;
    }
}
