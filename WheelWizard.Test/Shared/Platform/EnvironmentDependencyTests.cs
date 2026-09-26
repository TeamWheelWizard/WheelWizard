using Testably.Abstractions.Testing;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Recomp;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Shared.Platform;

public class EnvironmentDependencyTests
{
    [Theory]
    [InlineData(true, true, "io.example.App", true)]
    [InlineData(false, true, "io.example.App", false)]
    [InlineData(true, false, "io.example.App", false)]
    [InlineData(true, true, "", false)]
    public void SandboxDetection_RequiresLinuxMarkerAndAppId(bool linux, bool marker, string appId, bool expected)
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        if (marker)
            fs.File.WriteAllText("/.flatpak-info", "sandbox");
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(linux);
        environment.GetEnvironmentVariable("FLATPAK_ID").Returns(appId);
        Assert.Equal(expected, environment.IsFlatpakSandboxed(fs));
    }

    [Fact]
    public void ShellQuoting_UsesTargetShellRules_ForApostrophesAndMetacharacters()
    {
        const string path = "a'b $value; file";
        Assert.Equal("'a'\\''b $value; file'", ShellQuoting.QuoteUnixArgument(path));
        Assert.Equal("'a''b $value; file'", ShellQuoting.QuotePowerShellArgument(path));
        Assert.Equal("\"folder with spaces\"", ShellQuoting.QuoteArgument("folder with spaces", true));
    }

    [Fact]
    public void PackageManagerDetection_PreservesPriorityAndFallsBackWhenUnavailable()
    {
        var processes = Substitute.For<IUnixProcessService>();
        var available = new HashSet<string> { "command -v -- apt", "command -v -- dnf" };
        processes
            .Run("/usr/bin/env", Arg.Any<IEnumerable<string>>(), out Arg.Any<string>(), out Arg.Any<string>())
            .Returns(call => Ok(available.Contains(call.Arg<IEnumerable<string>>().Last()) ? 0 : 1));
        var commands = new UnixCommandService(processes);
        Assert.Equal("apt install -y", commands.DetectPackageManagerInstallCommand());
        available.Remove("command -v -- apt");
        Assert.Equal("dnf -y install", commands.DetectPackageManagerInstallCommand());
        available.Clear();
        Assert.Equal(string.Empty, commands.DetectPackageManagerInstallCommand());
    }

    [Fact]
    public void CommandProbeFailure_IsUnavailable()
    {
        var processes = Substitute.For<IUnixProcessService>();
        processes
            .Run("/usr/bin/env", Arg.Any<IEnumerable<string>>(), out Arg.Any<string>(), out Arg.Any<string>())
            .Returns((OperationResult<int>)Fail("cannot start shell"));
        Assert.False(new UnixCommandService(processes).IsCommandAvailable("dolphin-emu"));
    }

    [Fact]
    public void DolphinCommandValidation_UsesInjectedUnixEnvironmentAndProbe()
    {
        var fs = new MockFileSystem(options => options.SimulatingOperatingSystem(SimulationMode.Linux));
        var environment = Substitute.For<IRuntimeEnvironment>();
        environment.IsLinux.Returns(true);
        var commands = Substitute.For<IUnixCommandService>();
        commands.IsCommandAvailable("custom-dolphin").Returns(true);
        using var settings = new SettingsManager(
            Substitute.For<IWhWzSettingManager>(),
            Substitute.For<IDolphinSettingManager>(),
            Substitute.For<IRecompSettingManager>(),
            fs,
            Substitute.For<ISettingsSignalBus>(),
            new DolphinPathResolver(fs, environment),
            Substitute.For<IApplicationDataLocation>(),
            Substitute.For<IRecompPaths>(),
            environment,
            commands
        );

        Assert.True(settings.DOLPHIN_LOCATION.Set("custom-dolphin", skipSave: true));
        Assert.False(settings.DOLPHIN_LOCATION.Set("missing-command", skipSave: true));
        Assert.Equal("custom-dolphin", settings.Get<string>(settings.DOLPHIN_LOCATION));
        commands.Received().IsCommandAvailable("missing-command");
    }
}
