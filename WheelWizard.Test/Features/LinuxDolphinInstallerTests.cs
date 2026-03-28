using WheelWizard.DolphinInstaller;
using WheelWizard.Shared;

namespace WheelWizard.Test.Features;

public class LinuxDolphinInstallerTests
{
    private readonly ILinuxCommandEnvironment _commandEnvironment;
    private readonly ILinuxProcessService _processService;
    private readonly LinuxDolphinInstaller _installer;

    public LinuxDolphinInstallerTests()
    {
        _commandEnvironment = Substitute.For<ILinuxCommandEnvironment>();
        _processService = Substitute.For<ILinuxProcessService>();
        _installer = new LinuxDolphinInstaller(_commandEnvironment, _processService);
    }

    [Fact]
    public void IsDolphinInstalledInFlatpak_ReturnsTrue_WhenFlatpakInfoExitCodeIsZero()
    {
        _processService.Run("flatpak", "info org.DolphinEmu.dolphin-emu").Returns(Ok(0));

        var result = _installer.IsDolphinInstalledInFlatpak();

        Assert.True(result);
    }

    [Fact]
    public void IsDolphinInstalledInFlatpak_ReturnsFalse_WhenFlatpakInfoExitCodeIsNonZero()
    {
        _processService.Run("flatpak", "info org.DolphinEmu.dolphin-emu").Returns(Ok(1));

        var result = _installer.IsDolphinInstalledInFlatpak();

        Assert.False(result);
    }

    [Fact]
    public async Task InstallFlatpak_ReturnsFailure_WhenPackageManagerCannotBeDetected()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(false);
        _commandEnvironment.DetectPackageManagerInstallCommand().Returns(string.Empty);

        var result = await _installer.InstallFlatpak();

        Assert.True(result.IsFailure);
        Assert.Contains("Unsupported Linux distribution", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpak_ReturnsFailure_WhenPkexecIsUnauthorized()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(false);
        _commandEnvironment.DetectPackageManagerInstallCommand().Returns("apt-get install -y");
        _processService
            .RunWithProgressAsync("pkexec", "apt-get install -y flatpak", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult<OperationResult<int>>(Ok(126)));

        var result = await _installer.InstallFlatpak();

        Assert.True(result.IsFailure);
        Assert.Contains("administrator", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpak_ReturnsSuccess_WhenInstallCompletesAndCommandBecomesAvailable()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(false, true);
        _commandEnvironment.DetectPackageManagerInstallCommand().Returns("apt-get install -y");
        _processService
            .RunWithProgressAsync("pkexec", "apt-get install -y flatpak", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult<OperationResult<int>>(Ok(0)));

        var result = await _installer.InstallFlatpak();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenDolphinInstallCommandFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .RunWithProgressAsync("pkexec", "flatpak --system install -y org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult<OperationResult<int>>(Ok(1)));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsFailure);
        Assert.Contains("exit code 1", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenWarmupLaunchFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .RunWithProgressAsync("pkexec", "flatpak --system install -y org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult<OperationResult<int>>(Ok(0)));
        _processService
            .LaunchAndStopAsync("flatpak", "run org.DolphinEmu.dolphin-emu", TimeSpan.FromSeconds(4))
            .Returns(Task.FromResult<OperationResult>(Fail("Launch failed")));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsFailure);
        Assert.Equal("Launch failed", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsSuccess_WhenInstallAndWarmupSucceed()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .RunWithProgressAsync("pkexec", "flatpak --system install -y org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult<OperationResult<int>>(Ok(0)));
        _processService
            .LaunchAndStopAsync("flatpak", "run org.DolphinEmu.dolphin-emu", TimeSpan.FromSeconds(4))
            .Returns(Task.FromResult<OperationResult>(Ok()));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsSuccess);
    }
}
