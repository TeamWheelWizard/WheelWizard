using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Mods;

namespace WheelWizard.Test.Features;

public class ManagedResourcePathTests
{
    [Fact]
    public async Task ModLoading_UsesRelocatedRoot_AfterServiceConstruction()
    {
        var fs = new MockFileSystem();
        var location = Substitute.For<IApplicationDataLocation>();
        var root = fs.Path.GetFullPath("/first");
        location.DirectoryPath.Returns(_ => root);
        var service = new ModInstallationService(fs, new ModPaths(location, fs));

        Assert.True((await service.LoadModsAsync()).IsSuccess);
        root = fs.Path.GetFullPath("/relocated");
        Assert.True((await service.LoadModsAsync()).IsSuccess);

        Assert.True(fs.Directory.Exists(fs.Path.Combine(root, "Mods")));
    }
}
