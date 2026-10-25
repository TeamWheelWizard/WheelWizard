using System.Buffers.Binary;
using System.Text;
using WheelWizard.CustomMusic.Catalog;

namespace WheelWizard.Test.Features.CustomMusic;

public class CustomMusicCatalogTests
{
    [Fact]
    public void GameCatalog_ListsEveryCourseArenaMenuAndJingleOnce()
    {
        var slots = GameMusicCatalog.Build();

        Assert.Equal(slots.Count, slots.Select(slot => slot.Key).Distinct().Count());
        // 32 courses where Luigi Circuit and Mario Circuit share music, plus Galaxy Colosseum.
        Assert.Equal(32, slots.Count(slot => slot.Category == MusicCategory.Course));
        Assert.Equal(10, slots.Count(slot => slot.Category == MusicCategory.Arena));
        Assert.All(slots.Where(slot => slot.Category is MusicCategory.Course or MusicCategory.Arena && slot.CourseId != GameMusicCatalog.GalaxyColosseumCourseId), slot => Assert.True(slot.HasFinalLap));
        Assert.Equal(slots.OrderBy(slot => slot.Order).Select(slot => slot.Key), slots.Select(slot => slot.Key));
    }

    [Fact]
    public void GameCatalog_UsesTheGamesExactFileNames()
    {
        var slots = GameMusicCatalog.Build().ToDictionary(slot => slot.Key);

        var luigi = slots["game:n_circuit32"];
        Assert.Equal("Luigi Circuit / Mario Circuit", luigi.Name);
        Assert.Equal(["n_Circuit32_n.brstm"], luigi.FileNames);
        Assert.Equal(["n_Circuit32_f.brstm"], luigi.FinalLapFileNames);
        Assert.Equal(["n_Kinoko_F.brstm"], slots["game:n_kinoko"].FinalLapFileNames);

        var waiting = slots["game:o_wi-fi_waiting32"];
        Assert.Equal(MusicCategory.Menu, waiting.Category);
        Assert.Equal(["o_Wi-Fi_waiting32.brstm"], waiting.FileNames);
        Assert.False(waiting.HasFinalLap);

        Assert.False(slots["game:o_start32_fan"].Loops);
        Assert.Equal(["wifi_lobby_bg.brstm"], slots["menu:wifi_lobby_bg"].FileNames);
    }

    [Theory]
    [InlineData(0x08, "game:n_circuit32")]
    [InlineData(0x00, "game:n_circuit32")]
    [InlineData(0x02, "game:n_kinoko")]
    [InlineData(GameMusicCatalog.GalaxyColosseumCourseId, "game:n_bossmi_32")]
    [InlineData(0x7F, null)]
    public void GameCatalog_FindsTheMusicOfACourseId(int courseId, string? key) => Assert.Equal(key, GameMusicCatalog.KeyForCourseId(courseId));

    [Theory]
    [InlineData("n_Circuit32_n", "n_Circuit32")]
    [InlineData("STRM_N_KOOPA_F", "STRM_N_KOOPA")]
    [InlineData("title_bg", "title_bg")]
    public void GameCatalog_StripsLapSuffixes(string name, string expected) => Assert.Equal(expected, GameMusicCatalog.StripLapSuffix(name));

    [Fact]
    public void PulsarConfigReader_ReadsCupsTracksVariantsAndFileNames()
    {
        var config = new PulsarConfigBuilder()
            .Cup("SNES Cup")
            .Track(0x08, 0x18, "SNES Mario Circuit 1", version: "v2")
            .Track(0x01, 0x08, "SNES Donut Plains 1")
            .Track(0x02, 0x1A, "SNES Ghost Valley 1", variants: [(0x02, 0x1A, "SNES Ghost Valley 1 (Night)")])
            .Track(0x04, 0x0F, "SNES Bowser Castle 1")
            .Cup("N64 Cup") // an odd cup count is stored rounded up to an even number
            .Track(0x00, 0x1C, "N64 Luigi Raceway")
            .Track(0x05, 0x05, "N64 Moo Moo Farm")
            .Track(0x06, 0x06, "N64 Koopa Troopa Beach")
            .Track(0x07, 0x07, "N64 Kalimari Desert")
            .Cup("GBA Cup")
            .Track(0x08, 0x08, "GBA Peach Circuit")
            .Track(0x09, 0x09, "GBA Riverside Park")
            .Track(0x0A, 0x0A, "GBA Cheese Land")
            .Track(0x0B, 0x0B, "GBA Sky Garden")
            .File(0, "rMC1")
            .File(2, "rGV1")
            .File((1 << 12) + 2, "rGV1Night")
            .File(4, "nLR")
            .Build();

        var result = PulsarConfigReader.Read(config);

        Assert.Equal(["SNES Cup", "N64 Cup", "GBA Cup"], result.CupNames);
        Assert.Equal(12, result.Tracks.Count);

        var mc1 = result.Tracks[0];
        Assert.Equal("SNES Mario Circuit 1 v2", mc1.Name); // text around colour codes is kept, the codes themselves are dropped
        Assert.Equal("rMC1", mc1.FileName);
        Assert.Equal(0x18, mc1.MusicSlot);
        Assert.Null(result.Tracks[1].FileName);

        var ghostValley = result.Tracks[2];
        Assert.Equal("SNES Ghost Valley 1 (all versions)", ghostValley.Name);
        var night = Assert.Single(ghostValley.Variants);
        Assert.Equal("SNES Ghost Valley 1 (Night)", night.Name);
        Assert.Equal("rGV1Night", night.FileName);
        Assert.Equal(0x1A, night.MusicSlot);

        Assert.Equal(1, result.Tracks[4].CupIndex);
        Assert.Equal("GBA Sky Garden", result.Tracks[11].Name);
        Assert.Empty(result.Tracks[11].Variants);
    }

    [Fact]
    public void PulsarConfigReader_KeepsNamesThatAreSplitByColourCodes()
    {
        var config = new PulsarConfigBuilder()
            .Cup("3DS Cup")
            .Track(0x08, 0x08, "\u00013DS\u0001 Toad Circuit")
            .Track(0x01, 0x01, "\u00013DS\u0001 Rainbow Road")
            .Track(0x02, 0x02, "3DS Music Park")
            .Track(0x03, 0x03, "3DS Piranha Plant Slide")
            .Build();

        var names = PulsarConfigReader.Read(config).Tracks.Select(track => track.Name);

        Assert.Equal(["3DS Toad Circuit", "3DS Rainbow Road", "3DS Music Park", "3DS Piranha Plant Slide"], names);
    }

    [Fact]
    public void PulsarConfigReader_RejectsOtherFiles() => Assert.Throws<InvalidDataException>(() => PulsarConfigReader.Read(Encoding.ASCII.GetBytes("RSTM and some more bytes here....")));

    [Fact]
    public void PulsarConfigReader_FileSectionKeepsOnlyTheFileName()
    {
        var files = PulsarConfigReader.ReadFileSection(Encoding.UTF8.GetBytes("FILE\n1A=uMKS|expert1|expert2\n1001A=uMKSv2|a\nbroken line\n0?cupicon\n"));

        Assert.Equal("uMKS", files[0x1A]);
        Assert.Equal("uMKSv2", files[0x1001A]);
        Assert.Equal(2, files.Count);
    }
}
