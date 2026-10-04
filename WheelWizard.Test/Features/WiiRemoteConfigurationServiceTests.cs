using Testably.Abstractions.Testing;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Test.Features;

public class WiiRemoteConfigurationServiceTests
{
    [Theory]
    [InlineData(true, "Source = 1")]
    [InlineData(false, "Source = 0")]
    public void SetVirtualRemoteEnabled_ChangesOnlyFirstRemoteInSuppliedDirectory(bool enabled, string expected)
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/selected");
        fs.Directory.CreateDirectory("/other");
        string[] original = ["# keep", "[Wiimote1]", "Source = 2", "Extension = Classic", "[Wiimote2]", "Source = 2"];
        fs.File.WriteAllLines("/selected/WiimoteNew.ini", original);
        fs.File.WriteAllLines("/other/WiimoteNew.ini", original);

        new WiiRemoteConfigurationService(fs).SetVirtualRemoteEnabled("/selected", enabled);

        Assert.Equal(
            ["# keep", "[Wiimote1]", expected, "Extension = Classic", "[Wiimote2]", "Source = 2"],
            fs.File.ReadAllLines("/selected/WiimoteNew.ini")
        );
        Assert.Equal(original, fs.File.ReadAllLines("/other/WiimoteNew.ini"));
    }

    [Fact]
    public void MissingSource_IsInsertedIntoFirstRemoteSection()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllLines("/config/WiimoteNew.ini", ["[Wiimote1]", "Extension = Classic", "[Wiimote2]", "Source = 2"]);

        new WiiRemoteConfigurationService(fs).SetVirtualRemoteEnabled("/config", true);

        Assert.Equal(
            ["[Wiimote1]", "Source = 1", "Extension = Classic", "[Wiimote2]", "Source = 2"],
            fs.File.ReadAllLines("/config/WiimoteNew.ini")
        );
    }

    [Fact]
    public void MissingRemoteSection_IsAddedWithoutChangingAnotherRemote()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/config");
        fs.File.WriteAllLines("/config/WiimoteNew.ini", ["[Wiimote2]", "Source = 2"]);

        new WiiRemoteConfigurationService(fs).SetVirtualRemoteEnabled("/config", false);

        Assert.Equal(["[Wiimote2]", "Source = 2", "[Wiimote1]", "Source = 0"], fs.File.ReadAllLines("/config/WiimoteNew.ini"));
    }

    [Fact]
    public void MissingConfiguration_ReportsFailureWithoutCreatingFile()
    {
        var fs = new MockFileSystem();

        Assert.Throws<FileNotFoundException>(() => new WiiRemoteConfigurationService(fs).SetVirtualRemoteEnabled("/missing", true));
        Assert.False(fs.File.Exists("/missing/WiimoteNew.ini"));
    }
}
