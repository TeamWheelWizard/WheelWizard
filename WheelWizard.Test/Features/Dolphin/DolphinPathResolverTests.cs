using Testably.Abstractions.Testing;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Shared.Platform;

namespace WheelWizard.Test.Features.Dolphin;

public class DolphinPathResolverTests
{
    [Theory]
    [InlineData(SimulationMode.Windows, false)]
    [InlineData(SimulationMode.MacOS, false)]
    [InlineData(SimulationMode.Linux, true)]
    public void CustomUserDirectory_UsesConfigChild(SimulationMode platform, bool isLinux)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(platform));
        var environment = EnvironmentFor(fs, isLinux);
        var userDirectory = fs.Path.GetFullPath("users/portable-dolphin");
        var resolver = new DolphinPathResolver(fs, environment);

        var paths = resolver.Resolve("dolphin-emu", userDirectory);

        Assert.Equal(fs.Path.Combine(userDirectory, "Config"), paths.ConfigFolderPath);
    }

    [Fact]
    public void NativeLinux_UsesXdgConfigDirectory_ForMatchingDataDirectory()
    {
        var (fs, environment) = LinuxEnvironment();
        var resolver = new DolphinPathResolver(fs, environment);

        var paths = resolver.Resolve("dolphin-emu", "/home/player/.local/share/dolphin-emu");

        Assert.Equal("/home/player/.config/dolphin-emu", paths.ConfigFolderPath);
        Assert.True(paths.IsLinuxDolphinConfigSplit());
        Assert.Equal("/custom/Config", resolver.Resolve("dolphin-emu", "/custom").ConfigFolderPath);
        Assert.Equal("/home/player/.config/dolphin-emu", paths.ConfigFolderPath);
    }

    [Fact]
    public void ExternalFlatpak_UsesForkAppId_AndDoesNotSelectNativeXdgConfig()
    {
        var (fs, environment) = LinuxEnvironment();
        var resolver = new DolphinPathResolver(fs, environment);
        const string command = "flatpak run --branch=stable org.example.DolphinFork";

        var fork = resolver.Resolve(command, "/home/player/.var/app/org.example.DolphinFork/data/dolphin-emu");
        var nativeData = resolver.Resolve(command, "/home/player/.local/share/dolphin-emu");

        Assert.Equal("/home/player/.var/app/org.example.DolphinFork/config/dolphin-emu", fork.ConfigFolderPath);
        Assert.Equal("/home/player/.local/share/dolphin-emu/Config", nativeData.ConfigFolderPath);
    }

    [Fact]
    public void SandboxedWheelWizard_DetectsForkFromUserDirectory_AndUsesBundledCommand()
    {
        var (fs, environment) = LinuxEnvironment(sandboxed: true);
        var paths = new DolphinPathResolver(fs, environment).Resolve(
            "ignored-command",
            "/home/player/.var/app/org.example.DolphinFork/data/dolphin-emu"
        );

        Assert.Equal("/app/bin/dolphin-emu-wrapper", paths.DolphinFilePath);
        Assert.Equal("/home/player/.var/app/org.example.DolphinFork/config/dolphin-emu", paths.ConfigFolderPath);
        Assert.Contains(
            "/home/player/.var/app/org.example.WheelWizard/data-dolphin-emu/dolphin-emu",
            paths.LinuxFlatpakSandboxedDolphinUserFolderBlockList
        );
    }

    [Theory]
    [InlineData("/host-data", "/host-config", "/host-data/dolphin-emu", "/host-config/dolphin-emu")]
    [InlineData("relative-data", "relative-config", "/home/player/.local/share/dolphin-emu", "/home/player/.config/dolphin-emu")]
    public void SandboxedWheelWizard_UsesAbsoluteHostXdgValues_OrHomeFallback(
        string dataHome,
        string configHome,
        string userFolder,
        string expectedConfig
    )
    {
        var (fs, environment) = LinuxEnvironment(sandboxed: true);
        environment.GetEnvironmentVariable("HOST_XDG_DATA_HOME").Returns(dataHome);
        environment.GetEnvironmentVariable("HOST_XDG_CONFIG_HOME").Returns(configHome);

        var paths = new DolphinPathResolver(fs, environment).Resolve("ignored", userFolder);

        Assert.Equal(expectedConfig, paths.ConfigFolderPath);
    }

    [Fact]
    public void EmptyUserDirectory_FallsBackWithoutThrowing()
    {
        var (fs, environment) = LinuxEnvironment();

        var paths = new DolphinPathResolver(fs, environment).Resolve("", "");

        Assert.Equal("Config", paths.ConfigFolderPath);
    }

    private static (MockFileSystem, IRuntimeEnvironment) LinuxEnvironment(bool sandboxed = false)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var environment = EnvironmentFor(fs, true);
        if (sandboxed)
        {
            fs.File.WriteAllText("/.flatpak-info", "");
            environment.GetEnvironmentVariable("FLATPAK_ID").Returns("org.example.WheelWizard");
        }
        return (fs, environment);
    }

    private static IRuntimeEnvironment EnvironmentFor(MockFileSystem fs, bool isLinux)
    {
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(isLinux);
        environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Returns(fs.Path.GetFullPath("/home/player"));
        environment.GetFolderPath(Environment.SpecialFolder.ApplicationData).Returns(fs.Path.GetFullPath("/home/player/.config"));
        environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData).Returns(fs.Path.GetFullPath("/home/player/.local/share"));
        return environment;
    }
}
