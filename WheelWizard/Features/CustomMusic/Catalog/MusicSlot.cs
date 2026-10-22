namespace WheelWizard.CustomMusic.Catalog;

public enum MusicCategory
{
    /// <summary>A Retro Rewind race track (retro and custom tracks from ConfigRT/ConfigCT).</summary>
    RetroRewindTrack,

    /// <summary>A Retro Rewind battle arena (ConfigBT).</summary>
    RetroRewindArena,

    /// <summary>One of the original 32 courses (or Galaxy Colosseum).</summary>
    Course,

    /// <summary>One of the original 10 battle arenas.</summary>
    Arena,

    /// <summary>Menu background music.</summary>
    Menu,

    /// <summary>Intros, fanfares, results and other jingles.</summary>
    Jingle,
}

/// <summary>
/// One piece of music the player can replace: what it is called, which files the game loads for it,
/// and where the original can be found for loudness matching.
/// </summary>
public sealed record MusicSlot
{
    /// <summary>Stable identifier, used to remember customisations between sessions and Retro Rewind updates.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }
    public required MusicCategory Category { get; init; }

    /// <summary>Section heading for the default (cup) order, e.g. "Mushroom Cup".</summary>
    public required string Group { get; init; }

    /// <summary>Position in the default order (cup order, then track order).</summary>
    public required int Order { get; init; }

    /// <summary>File names (no folders) that receive the normal version.</summary>
    public required IReadOnlyList<string> FileNames { get; init; }

    /// <summary>File names that receive the final lap version; empty when the music has no final lap.</summary>
    public IReadOnlyList<string> FinalLapFileNames { get; init; } = [];

    /// <summary>Whether the replacement should loop (courses and menus) or play once (jingles).</summary>
    public bool Loops { get; init; } = true;

    /// <summary>The original game's file (in /sound/strm) whose loudness the replacement is matched to.</summary>
    public string? GameFileName { get; init; }

    /// <summary>A BRSTM shipped by Retro Rewind that currently plays here, used for loudness matching.</summary>
    public string? RetroRewindFilePath { get; init; }

    /// <summary>Short explanation of what currently plays here (e.g. "Uses Luigi Circuit's music").</summary>
    public string? Detail { get; init; }

    /// <summary>The original course this slot belongs to (Mario Kart Wii course id), when there is one.</summary>
    public int? CourseId { get; init; }

    /// <summary>Extra search terms (file names, alternative names).</summary>
    public string SearchText { get; init; } = string.Empty;

    public bool HasFinalLap => FinalLapFileNames.Count > 0;

    /// <summary>
    /// Retro Rewind's own menu music is played through Koopa Cape's sound with every track of the file at full
    /// volume, so all tracks are heard at once; everything else is heard through its first track.
    /// </summary>
    public bool PlaysAllTracks => Key.StartsWith("menu:", StringComparison.Ordinal);

    public IEnumerable<string> AllFileNames => FileNames.Concat(FinalLapFileNames);
}

public sealed record MusicCatalog(IReadOnlyList<MusicSlot> Slots, RetroRewindCatalogState RetroRewind)
{
    public static MusicCatalog Empty { get; } = new([], RetroRewindCatalogState.NotInstalled);
}

public enum RetroRewindCatalogState
{
    Loaded,
    NotInstalled,
    Unreadable,
}
