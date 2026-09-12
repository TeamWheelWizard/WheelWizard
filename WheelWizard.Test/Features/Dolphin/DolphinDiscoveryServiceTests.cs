using Testably.Abstractions.Testing;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.Dolphin;

public class DolphinDiscoveryServiceTests
{
    [Fact]
    public void PortableInstall_UsesSelectedExecutable_AndTakesPriorityOverRegistry()
    {
        var (fs, environment, registry, service) = Create(SimulationMode.Windows);
        fs.Directory.CreateDirectory("/selected/User");
        fs.File.WriteAllText("/selected/portable.txt", "");
        fs.Directory.CreateDirectory("/registered");
        registry.UserConfigPath.Returns(fs.Path.GetFullPath("/registered"));

        var result = service.FindUserDirectory(fs.Path.GetFullPath("/selected/Dolphin.exe"), "/previous-user");

        Assert.Equal(fs.Path.GetFullPath("/selected/User"), result);
    }

    [Fact]
    public void WindowsLocalUserFlag_EnablesAdjacentDirectoryWithoutPortableMarker()
    {
        var (fs, _, registry, service) = Create(SimulationMode.Windows);
        fs.Directory.CreateDirectory("/selected/User");
        fs.Directory.CreateDirectory("/registered");
        registry.UserConfigPath.Returns(fs.Path.GetFullPath("/registered"));
        registry.UseLocalUserDirectory.Returns(true);

        Assert.Equal(fs.Path.GetFullPath("/selected/User"), service.FindUserDirectory(fs.Path.GetFullPath("/selected/Dolphin.exe"), ""));
    }

    [Fact]
    public void WindowsRegistryPath_PrecedesDocuments_AndMissingRegistryDirectoryFallsBack()
    {
        var (fs, environment, registry, service) = Create(SimulationMode.Windows);
        fs.Directory.CreateDirectory("/registered");
        var documents = fs.Path.Combine(environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator");
        fs.Directory.CreateDirectory(documents);
        registry.UserConfigPath.Returns(fs.Path.GetFullPath("/registered").Replace('\\', '/'));

        Assert.Equal(fs.Path.GetFullPath("/registered"), service.FindUserDirectory("Dolphin.exe", ""));
        registry.UserConfigPath.Returns("/not-installed");
        Assert.Equal(documents, service.FindUserDirectory("Dolphin.exe", ""));
    }

    [Fact]
    public void NativeLinux_PrefersLegacyDirectory_ThenRequiresBothXdgDirectories()
    {
        var (fs, _, _, service) = Create(SimulationMode.Linux);
        fs.Directory.CreateDirectory("/home/player/.dolphin-emu");
        fs.Directory.CreateDirectory("/home/player/.local/share/dolphin-emu");

        Assert.Equal("/home/player/.dolphin-emu", service.FindUserDirectory("dolphin-emu", ""));
        fs.Directory.Delete("/home/player/.dolphin-emu");
        Assert.Null(service.FindUserDirectory("dolphin-emu", ""));
        fs.Directory.CreateDirectory("/home/player/.config/dolphin-emu");
        Assert.Equal("/home/player/.local/share/dolphin-emu", service.FindUserDirectory("dolphin-emu", ""));
    }

    [Fact]
    public void LinuxFlatpakFork_UsesSelectedCommandAppId()
    {
        var (fs, _, _, service) = Create(SimulationMode.Linux);
        fs.Directory.CreateDirectory("/home/player/.var/app/org.example.DolphinFork/data/dolphin-emu");
        fs.Directory.CreateDirectory("/home/player/.dolphin-emu");

        Assert.Equal(
            "/home/player/.var/app/org.example.DolphinFork/data/dolphin-emu",
            service.FindUserDirectory("flatpak run org.example.DolphinFork", "")
        );
        Assert.Equal("/home/player/.dolphin-emu", service.FindUserDirectory("dolphin-emu", ""));
    }

    [Fact]
    public void SandboxedLinux_IgnoresEmbeddedDirectory_AndFallsBackToHostXdg()
    {
        var (fs, environment, _, service) = Create(SimulationMode.Linux);
        fs.Directory.CreateDirectory("user");
        fs.File.WriteAllText("/.flatpak-info", "");
        environment.GetEnvironmentVariable("FLATPAK_ID").Returns("org.example.WheelWizard");
        environment.GetEnvironmentVariable("HOST_XDG_DATA_HOME").Returns("/host/data");
        environment.GetEnvironmentVariable("HOST_XDG_CONFIG_HOME").Returns("/host/config");
        fs.Directory.CreateDirectory("/host/data/dolphin-emu");
        fs.Directory.CreateDirectory("/host/config/dolphin-emu");

        Assert.Equal("/host/data/dolphin-emu", service.FindUserDirectory("ignored", ""));
    }

    [Fact]
    public void MacApplication_PrefersSystemInstall_ThenUserInstall()
    {
        var (fs, _, _, service) = Create(SimulationMode.MacOS);
        const string systemApp = "/Applications/Dolphin.app/Contents/MacOS/Dolphin";
        const string userApp = "/home/player/Applications/Dolphin.app/Contents/MacOS/Dolphin";
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(systemApp)!);
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(userApp)!);
        fs.File.WriteAllText(systemApp, "");
        fs.File.WriteAllText(userApp, "");

        Assert.Equal(systemApp, service.FindApplication());
        fs.File.Delete(systemApp);
        Assert.Equal(userApp, service.FindApplication());
        fs.File.Delete(userApp);
        Assert.Null(service.FindApplication());
    }

    private static (MockFileSystem, IRuntimeEnvironment, IDolphinRegistrySettings, DolphinDiscoveryService) Create(SimulationMode platform)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(platform));
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsWindows.Returns(platform == SimulationMode.Windows);
        environment.IsLinux.Returns(platform == SimulationMode.Linux);
        environment.IsMacOS.Returns(platform == SimulationMode.MacOS);
        environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Returns(fs.Path.GetFullPath("/home/player"));
        environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).Returns(fs.Path.GetFullPath("/home/player/Documents"));
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns(fs.Path.GetFullPath("/home/player/.config"));
        environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Returns(fs.Path.GetFullPath("/home/player/.local/share"));
        var registry = Substitute.For<IDolphinRegistrySettings>();
        return (
            fs,
            environment,
            registry,
            new DolphinDiscoveryService(fs, environment, new DolphinPathResolver(fs, environment), registry)
        );
    }
}
