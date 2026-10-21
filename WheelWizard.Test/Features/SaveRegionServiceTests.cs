using Testably.Abstractions.Testing;
using WheelWizard.DiscImages;
using WheelWizard.DiscImages.Domain;
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
        fixture
            .Discs.ReadInfo("/game.rvz")
            .Returns((OperationResult<DiscImageInfo>)new DiscImageInfo("RMCP01", "MarioKartWii", 0, DiscImageFormat.Rvz));

        Assert.Equal(MarioKartWiiEnums.Regions.Europe, fixture.Service.ResolveActiveRegion("/saves"));
    }

    [Fact]
    public void ActiveRegion_IgnoresOtherGames()
    {
        var fixture = Create(new MockFileSystem(), MarioKartWiiEnums.Regions.Japan);
        fixture
            .Discs.ReadInfo("/game.rvz")
            .Returns((OperationResult<DiscImageInfo>)new DiscImageInfo("RWSJ01", "MARIO & SONIC", 0, DiscImageFormat.Rvz));

        Assert.Equal(MarioKartWiiEnums.Regions.Japan, fixture.Service.ResolveActiveRegion("/saves"));
    }

    [Fact]
    public void ActiveRegion_WithoutReadableGame_FallsBackToFirstSave()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/saves/RMCK");
        fs.File.WriteAllText("/saves/RMCK/rksys.dat", "save");
        var fixture = Create(fs);
        fixture.Discs.ReadInfo(Arg.Any<string>()).Returns((OperationResult<DiscImageInfo>)Fail("Game file not found"));

        Assert.Equal(MarioKartWiiEnums.Regions.Korea, fixture.Service.ResolveActiveRegion("/saves"));
    }

    private static (SaveRegionService Service, IDiscImageService Discs) Create(
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
        var discs = Substitute.For<IDiscImageService>();
        return (new SaveRegionService(fs, settings, discs), discs);
    }
}
