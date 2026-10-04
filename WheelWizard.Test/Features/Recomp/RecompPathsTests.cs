using System.Runtime.InteropServices;
using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Recomp;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.Recomp;

public class RecompPathsTests
{
    [Fact]
    public void WindowsLayout_KeepsRuntimeStateUnderPortableRoot_AndFollowsRelocation()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Windows));
        var location = Substitute.For<IApplicationDataLocation>();
        var root = fs.Path.GetFullPath("/first");
        location.DirectoryPath.Returns(_ => root);
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(true);
        var paths = new RecompPaths(location, fs, environment);

        Assert.True(paths.IsPortableInstall);
        Assert.Equal(fs.Path.Combine(root, "Recomp", "UserData", "Config.toml"), paths.ConfigFilePath);
        Assert.Equal(fs.Path.Combine(root, "Recomp", "UserData", "NAND"), paths.PrivateNandFolderPath);
        Assert.Equal(fs.Path.Combine(root, "Recomp", "Install", "WiiCompiled-Setup.exe"), paths.SetupFilePath);

        root = fs.Path.GetFullPath("/relocated");

        Assert.Equal(fs.Path.Combine(root, "Recomp", "UserData", "Config.toml"), paths.ConfigFilePath);
        Assert.Equal(fs.Path.Combine(root, "Recomp", "Nand"), paths.NandCopyFolderPath);
        Assert.Equal(fs.Path.Combine(root, "Recomp", "portable.txt"), paths.PortableMarkerFilePath);
    }

    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void LinuxLayout_LeavesBackendStateInXdgData_WhenLauncherDataMoves(Architecture architecture)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var location = Substitute.For<IApplicationDataLocation>();
        var root = "/first";
        location.DirectoryPath.Returns(_ => root);
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        environment.OSArchitecture.Returns(architecture);
        environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Returns("/xdg/data");
        var paths = new RecompPaths(location, fs, environment);

        Assert.False(paths.IsPortableInstall);
        Assert.Equal("/xdg/data/WiiCompiled/Config.toml", paths.ConfigFilePath);
        Assert.Equal("/xdg/data/WiiCompiled/NAND", paths.PrivateNandFolderPath);
        Assert.Equal("/xdg/data/WiiCompiled/install-state.json", paths.LinuxBackendStateFilePath);
        Assert.Equal("/first/Recomp/Install/WiiCompiled-Setup.AppImage", paths.SetupFilePath);

        root = "/relocated";

        Assert.Equal("/xdg/data/WiiCompiled/Config.toml", paths.ConfigFilePath);
        Assert.Equal("/xdg/data/WiiCompiled/install-state.json", paths.LinuxBackendStateFilePath);
        Assert.Equal("/relocated/Recomp/Cache", paths.CacheFolderPath);
        Assert.Equal("/relocated/Recomp/Install/install-state.json", paths.InstallStateFilePath);
        Assert.Equal("/relocated/Recomp/Nand", paths.NandCopyFolderPath);
    }
}
