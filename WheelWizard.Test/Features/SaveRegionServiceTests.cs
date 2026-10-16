using Testably.Abstractions.Testing;
using WheelWizard.GameFiles;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Features;

public class SaveRegionServiceTests
{
    [Fact]
    public void AvailableRegions_RequireSaveFileInSuppliedRoot()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/selected/RMCE");
        fs.File.WriteAllText("/selected/RMCE/rksys.dat", "save");
        fs.Directory.CreateDirectory("/selected/RMCP");
        fs.Directory.CreateDirectory("/other/RMCJ");
        fs.File.WriteAllText("/other/RMCJ/rksys.dat", "save");

        var regions = Create(fs).Service.GetAvailableRegions("/selected");

        Assert.Equal([MarioKartWiiEnums.Regions.America], regions);
    }

    [Fact]
    public void NoSaves_ReturnsNoneRegion()
    {
        Assert.Equal([MarioKartWiiEnums.Regions.None], Create(new MockFileSystem()).Service.GetAvailableRegions("/missing"));
    }

    [Theory]
    [InlineData("RMCE01", MarioKartWiiEnums.Regions.America)]
    [InlineData("RMCP01", MarioKartWiiEnums.Regions.Europe)]
    [InlineData("RMCJ01", MarioKartWiiEnums.Regions.Japan)]
    [InlineData("RMCK01", MarioKartWiiEnums.Regions.Korea)]
    [InlineData("RWSJ01", MarioKartWiiEnums.Regions.None)]
    [InlineData("RMC", MarioKartWiiEnums.Regions.None)]
    [InlineData(null, MarioKartWiiEnums.Regions.None)]
    public void GameRegion_FromGameId(string? gameId, MarioKartWiiEnums.Regions expected)
    {
        Assert.Equal(expected, GameRegion.FromGameId(gameId));
    }

    [Fact]
    public void ActiveRegion_FollowsGameFile_OverLastUsedRegion()
    {
        var fixture = Create(new MockFileSystem(), MarioKartWiiEnums.Regions.America);
        fixture.Game.GetRegion().Returns(MarioKartWiiEnums.Regions.Europe);

        Assert.Equal(MarioKartWiiEnums.Regions.Europe, fixture.Service.ResolveActiveRegion("/saves"));
    }

    [Fact]
    public void ActiveRegion_IgnoresOtherGames()
    {
        var fixture = Create(new MockFileSystem(), MarioKartWiiEnums.Regions.Japan);
        fixture.Game.GetRegion().Returns(MarioKartWiiEnums.Regions.None);

        Assert.Equal(MarioKartWiiEnums.Regions.Japan, fixture.Service.ResolveActiveRegion("/saves"));
    }

    [Fact]
    public void ActiveRegion_WithoutReadableGame_FallsBackToFirstSave()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/saves/RMCK");
        fs.File.WriteAllText("/saves/RMCK/rksys.dat", "save");
        var fixture = Create(fs);
        fixture.Game.GetRegion().Returns(MarioKartWiiEnums.Regions.None);

        Assert.Equal(MarioKartWiiEnums.Regions.Korea, fixture.Service.ResolveActiveRegion("/saves"));
    }

    private static (SaveRegionService Service, IGameFilesService Game) Create(
        MockFileSystem fs,
        MarioKartWiiEnums.Regions lastRegion = MarioKartWiiEnums.Regions.None
    )
    {
        var settings = Substitute.For<ISettingsManager>();
        var gamePath = new WhWzSetting<string>("GameLocation", "/game.rvz");
        var region = new WhWzSetting<MarioKartWiiEnums.Regions>("RR_Region", lastRegion);
        settings.GAME_LOCATION.Returns(gamePath);
        settings.RR_REGION.Returns(region);
        settings.Get(gamePath).Returns(_ => (string)gamePath.Get());
        settings.Get(region).Returns(_ => (MarioKartWiiEnums.Regions)region.Get());
        var game = Substitute.For<IGameFilesService>();
        return (new SaveRegionService(fs, settings, game), game);
    }
}
