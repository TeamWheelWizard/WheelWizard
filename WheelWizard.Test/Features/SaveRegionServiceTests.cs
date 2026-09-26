using Testably.Abstractions.Testing;
using WheelWizard.Models.Enums;
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

        var regions = new SaveRegionService(fs).GetAvailableRegions("/selected");

        Assert.Equal([MarioKartWiiEnums.Regions.America], regions);
    }

    [Fact]
    public void NoSaves_ReturnsNoneRegion()
    {
        Assert.Equal([MarioKartWiiEnums.Regions.None], new SaveRegionService(new MockFileSystem()).GetAvailableRegions("/missing"));
    }
}
