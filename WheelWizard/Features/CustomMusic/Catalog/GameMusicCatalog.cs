namespace WheelWizard.CustomMusic.Catalog;

/// <summary>
/// Every piece of streamed music in Mario Kart Wii, plus the menu music files Retro Rewind reads.
/// The file names (including their inconsistent capitalisation, e.g. n_Kinoko_F vs n_Daisy32_f) were
/// verified against the external file table in the retail revo_kart.brsar.
/// </summary>
public static class GameMusicCatalog
{
    public const int RetroRewindOrderBase = 0;
    public const int CourseOrderBase = 100_000;
    public const int ArenaOrderBase = 110_000;
    public const int MenuOrderBase = 120_000;
    public const int JingleOrderBase = 130_000;

    private sealed record CourseMusic(string Name, string Cup, int CourseId, string Normal, string Final, string? AlsoUsedBy = null);

    private static readonly CourseMusic[] Courses =
    [
        new("Luigi Circuit", "Mushroom Cup", 0x08, "n_Circuit32_n", "n_Circuit32_f", "Mario Circuit"),
        new("Moo Moo Meadows", "Mushroom Cup", 0x01, "n_Farm_n", "n_Farm_F"),
        new("Mushroom Gorge", "Mushroom Cup", 0x02, "n_Kinoko_n", "n_Kinoko_F"),
        new("Toad's Factory", "Mushroom Cup", 0x04, "STRM_N_FACTORY_N", "STRM_N_FACTORY_F"),
        new("Coconut Mall", "Flower Cup", 0x05, "n_Shopping32_n", "n_Shopping32_f"),
        new("DK Summit", "Flower Cup", 0x06, "n_Snowboard32_n", "n_Snowboard32_f"),
        new("Wario's Gold Mine", "Flower Cup", 0x07, "STRM_N_TRUCK_N", "STRM_N_TRUCK_F"),
        new("Daisy Circuit", "Star Cup", 0x09, "n_Daisy32_n", "n_Daisy32_f"),
        new("Koopa Cape", "Star Cup", 0x0F, "STRM_N_WATER_N", "STRM_N_WATER_F"),
        new("Maple Treeway", "Star Cup", 0x0B, "n_maple_n", "n_maple_F"),
        new("Grumble Volcano", "Star Cup", 0x03, "n_Volcano32_n", "n_Volcano32_f"),
        new("Dry Dry Ruins", "Special Cup", 0x0E, "STRM_N_DESERT_N", "STRM_N_DESERT_F"),
        new("Moonview Highway", "Special Cup", 0x0A, "STRM_N_RIDGEHIGHWAY_N", "STRM_N_RIDGEHIGHWAY_F"),
        new("Bowser's Castle", "Special Cup", 0x0C, "STRM_N_KOOPA_N", "STRM_N_KOOPA_F"),
        new("Rainbow Road", "Special Cup", 0x0D, "n_Rainbow32_n", "n_Rainbow32_f"),
        new("GCN Peach Beach", "Shell Cup", 0x10, "r_GC_Beach32_n", "r_GC_Beach32_f"),
        new("DS Yoshi Falls", "Shell Cup", 0x14, "r_DS_Jungle32_n", "r_DS_Jungle32_f"),
        new("SNES Ghost Valley 2", "Shell Cup", 0x19, "r_SFC_Obake32_n", "r_SFC_Obake32_f"),
        new("N64 Mario Raceway", "Shell Cup", 0x1A, "r_64_Circuit32_n", "r_64_Circuit32_f"),
        new("N64 Sherbet Land", "Banana Cup", 0x1B, "r_64_Sherbet32_n", "r_64_Sherbet32_f"),
        new("GBA Shy Guy Beach", "Banana Cup", 0x1F, "r_AGB_Beach32_n", "r_AGB_Beach32_f"),
        new("DS Delfino Square", "Banana Cup", 0x17, "r_DS_Town32_n", "r_DS_Town32_f"),
        new("GCN Waluigi Stadium", "Banana Cup", 0x12, "r_GC_Stadium32_n", "r_GC_Stadium32_f"),
        new("DS Desert Hills", "Leaf Cup", 0x15, "r_DS_Desert32_n", "r_DS_Desert32_f"),
        new("GBA Bowser Castle 3", "Leaf Cup", 0x1E, "r_AGB_Kuppa32_n", "r_AGB_Kuppa32_f"),
        new("N64 DK's Jungle Parkway", "Leaf Cup", 0x1D, "r_64_Jungle32_n", "r_64_Jungle32_f"),
        new("GCN Mario Circuit", "Leaf Cup", 0x11, "r_GC_Circuit32_n", "r_GC_Circuit32_f"),
        new("SNES Mario Circuit 3", "Lightning Cup", 0x18, "r_SFC_Circuit32_n", "r_SFC_Circuit32_f"),
        new("DS Peach Gardens", "Lightning Cup", 0x16, "r_DS_Garden32_n", "r_DS_Garden32_f"),
        new("GCN DK Mountain", "Lightning Cup", 0x13, "r_GC_Mountain32_n", "r_GC_Mountain32_f"),
        new("N64 Bowser's Castle", "Lightning Cup", 0x1C, "r_64_Kuppa32_n", "r_64_Kuppa32_f"),
    ];

    private static readonly CourseMusic[] Arenas =
    [
        new("Block Plaza", "Wii Arenas", 0x21, "n_block_n", "n_block_F"),
        new("Delfino Pier", "Wii Arenas", 0x20, "n_venice_n", "n_venice_F"),
        new("Chain Chomp Roulette", "Wii Arenas", 0x22, "n_casino_n", "n_casino_F"),
        new("Funky Stadium", "Wii Arenas", 0x23, "n_skate_n", "n_skate_F"),
        new("Thwomp Desert", "Wii Arenas", 0x24, "n_ryuusa_n", "n_ryuusa_F"),
        new("GCN Cookie Land", "Retro Arenas", 0x25, "r_GC_Battle32_n", "r_GC_Battle32_F"),
        new("DS Twilight House", "Retro Arenas", 0x26, "r_ds_battle_n", "r_ds_battle_F"),
        new("SNES Battle Course 4", "Retro Arenas", 0x27, "r_sfc_battle_n", "r_sfc_battle_F"),
        new("GBA Battle Course 3", "Retro Arenas", 0x28, "r_agb_battle_n", "r_agb_battle_F"),
        new("N64 Skyscraper", "Retro Arenas", 0x29, "r_64_battle_n", "r_64_battle_F"),
    ];

    /// <summary>Course id of Galaxy Colosseum, whose music has no final lap file.</summary>
    public const int GalaxyColosseumCourseId = 0x36;

    private sealed record SingleMusic(string Key, string Name, string Group, string File, bool Loops, string? Detail = null, bool IsGameFile = true);

    private static readonly SingleMusic[] Menus =
    [
        new("title_bg", "Title screen", "Menus", "title_bg", true, "Retro Rewind title screen music", IsGameFile: false),
        new("offline_bg", "Offline menus", "Menus", "offline_bg", true, "Single player and local multiplayer menus", IsGameFile: false),
        new("wifi_globe_bg", "Online menus", "Menus", "wifi_globe_bg", true, "Online menu and globe screens", IsGameFile: false),
        new("wifi_lobby_bg", "Online lobby", "Menus", "wifi_lobby_bg", true, "Online rooms and track voting", IsGameFile: false),
        new("o_Wi-Fi_waiting32", "Waiting to join a race", "Menus", "o_Wi-Fi_waiting32", true, "Also plays while watching ghost replays"),
        new("o_Option_32", "Options menu", "Menus", "o_Option_32", true),
        new("o_Ending_PartA_32", "Credits", "Menus", "o_Ending_PartA_32", true),
    ];

    private static readonly SingleMusic[] Jingles =
    [
        new("o_Crs_In_Fan", "Course intro", "Race", "o_Crs_In_Fan", true, "Plays during the course flyover"),
        new("o_Crs_In_Fan_Wifi", "Online course intro", "Race", "o_Crs_In_Fan_Wifi", true),
        new("o_Crs_In_Fan_battle", "Battle intro", "Race", "o_Crs_In_Fan_battle", true),
        new("o_Crs_In_Fan_mission", "Mission intro", "Race", "o_Crs_In_Fan_mission", true),
        new("o_Start32_fan", "Race start fanfare", "Race", "o_Start32_fan", false),
        new("o_Start2_32_fan", "Time Trial start fanfare", "Race", "o_Start2_32_fan", false),
        new("o_FanfareGP1_32", "1st place: fanfare and results", "Results", "o_FanfareGP1_32", true),
        new("o_FanfareGP1_only32", "1st place: fanfare only", "Results", "o_FanfareGP1_only32", false),
        new("o_FanfareGP2_32", "2nd to 5th place: fanfare and results", "Results", "o_FanfareGP2_32", true),
        new("o_FanfareGP2_only32", "2nd to 5th place: fanfare only", "Results", "o_FanfareGP2_only32", false),
        new("o_FanfareGPdame_32", "6th place or lower: fanfare and results", "Results", "o_FanfareGPdame_32", true),
        new("o_FanfareGPdame_only32", "6th place or lower: fanfare only", "Results", "o_FanfareGPdame_only32", false),
        new("o_FanfareTA1st_32", "Battle win: fanfare and results", "Results", "o_FanfareTA1st_32", true),
        new("o_FanfareTA1st_only32", "Battle win: fanfare only", "Results", "o_FanfareTA1st_only32", false),
        new("o_FanfareBTdraw_32", "Battle draw", "Results", "o_FanfareBTdraw_32", true),
        new("o_FanfareMIwin_32", "Victory: fanfare and results", "Results", "o_FanfareMIwin_32", true, "Mission win; Retro Rewind also uses it in Elimination"),
        new("o_FanfareMIwin_only32", "Victory: fanfare only", "Results", "o_FanfareMIwin_only32", false),
        new("o_FanfareMIlose_32", "Defeat: fanfare and results", "Results", "o_FanfareMIlose_32", true),
        new("o_FanfareMIlose_only32", "Defeat: fanfare only", "Results", "o_FanfareMIlose_only32", false),
        new("o_FanfareMIWinBoss_32", "Boss victory", "Results", "o_FanfareMIWinBoss_32", true),
        new("o_hyousyou_winningrun", "Trophy ceremony", "Results", "o_hyousyou_winningrun", true),
        new("o_hyousyou_normal", "Trophy ceremony (short)", "Results", "o_hyousyou_normal", true),
        new("o_hyousyou_cut", "Trophy ceremony (shorter)", "Results", "o_hyousyou_cut", true),
        new("o_hyousyou_lose", "Ceremony without a trophy", "Results", "o_hyousyou_lose", true),
        new("strm_me", "Unlock jingle", "Results", "strm_me", false, "New character, kart or cup unlocked"),
    ];

    public static string KeyForGameFile(string baseName) => "game:" + baseName.ToLowerInvariant();

    /// <summary>The slot key holding the music for an original course id (used for Retro Rewind music slots).</summary>
    public static string? KeyForCourseId(int courseId)
    {
        if (courseId == 0x00)
            courseId = 0x08; // Mario Circuit shares Luigi Circuit's music
        if (courseId == GalaxyColosseumCourseId)
            return KeyForGameFile("n_BossMI_32");
        var course = Courses.Concat(Arenas).FirstOrDefault(c => c.CourseId == courseId);
        return course == null ? null : KeyForGameFile(StripLapSuffix(course.Normal));
    }

    /// <summary>Display name of the original course with the given id.</summary>
    public static string? CourseName(int courseId)
    {
        if (courseId == 0x00)
            return "Mario Circuit";
        if (courseId == GalaxyColosseumCourseId)
            return "Galaxy Colosseum";
        return Courses.Concat(Arenas).FirstOrDefault(c => c.CourseId == courseId)?.Name;
    }

    public static IReadOnlyList<MusicSlot> Build()
    {
        var slots = new List<MusicSlot>();
        var order = CourseOrderBase;
        foreach (var course in Courses)
        {
            var name = course.AlsoUsedBy == null ? course.Name : $"{course.Name} / {course.AlsoUsedBy}";
            slots.Add(CourseSlot(course, name, MusicCategory.Course, order++, course.AlsoUsedBy == null ? null : $"Shared by {course.Name} and {course.AlsoUsedBy}"));
        }

        slots.Add(
            new MusicSlot
            {
                Key = KeyForGameFile("n_BossMI_32"),
                Name = "Galaxy Colosseum",
                Category = MusicCategory.Course,
                Group = "Other courses",
                Order = order,
                FileNames = ["n_BossMI_32.brstm"],
                GameFileName = "n_BossMI_32.brstm",
                CourseId = GalaxyColosseumCourseId,
                Detail = "Its final lap always speeds up instead of switching files",
                SearchText = "n_BossMI_32",
            }
        );

        order = ArenaOrderBase;
        foreach (var arena in Arenas)
            slots.Add(CourseSlot(arena, arena.Name, MusicCategory.Arena, order++, null));

        order = MenuOrderBase;
        foreach (var menu in Menus)
            slots.Add(SingleSlot(menu, MusicCategory.Menu, order++));

        order = JingleOrderBase;
        foreach (var jingle in Jingles)
            slots.Add(SingleSlot(jingle, MusicCategory.Jingle, order++));

        return slots;
    }

    private static MusicSlot CourseSlot(CourseMusic course, string name, MusicCategory category, int order, string? detail) =>
        new()
        {
            Key = KeyForGameFile(StripLapSuffix(course.Normal)),
            Name = name,
            Category = category,
            Group = course.Cup,
            Order = order,
            FileNames = [course.Normal + ".brstm"],
            FinalLapFileNames = [course.Final + ".brstm"],
            GameFileName = course.Normal + ".brstm",
            CourseId = course.CourseId,
            Detail = detail,
            SearchText = $"{course.Normal} {course.Final} {course.AlsoUsedBy}",
        };

    private static MusicSlot SingleSlot(SingleMusic music, MusicCategory category, int order) =>
        new()
        {
            Key = music.IsGameFile ? KeyForGameFile(music.File) : "menu:" + music.Key.ToLowerInvariant(),
            Name = music.Name,
            Category = category,
            Group = music.Group,
            Order = order,
            FileNames = [music.File + ".brstm"],
            Loops = music.Loops,
            GameFileName = music.IsGameFile ? music.File + ".brstm" : null,
            Detail = music.Detail,
            SearchText = music.File,
        };

    /// <summary>"n_Circuit32_n" -> "n_Circuit32", "STRM_N_KOOPA_N" -> "STRM_N_KOOPA".</summary>
    public static string StripLapSuffix(string name) =>
        name.EndsWith("_n", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_f", StringComparison.OrdinalIgnoreCase) ? name[..^2] : name;
}
