using Testably.Abstractions.Testing;
using WheelWizard.Shared.IO;

namespace WheelWizard.Test.Shared.IO;

public class DirectoryLinksTests
{
    [Fact]
    public void EnsureRelativeSymlink_UsesRelativeTarget()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));

        fs.EnsureRelativeSymlink("/links/dolphin", "/data/dolphin", createTarget: true);

        Assert.True(fs.Directory.Exists("/data/dolphin"));
        Assert.Equal("../data/dolphin", fs.FileInfo.New("/links/dolphin").LinkTarget);
    }

    [Fact]
    public void EnsureRelativeSymlink_RefusesToReplaceExistingFile()
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/links");
        fs.File.WriteAllText("/links/dolphin", "keep");

        Assert.Throws<IOException>(() => fs.EnsureRelativeSymlink("/links/dolphin", "/data/dolphin"));
        Assert.Equal("keep", fs.File.ReadAllText("/links/dolphin"));
    }
}
