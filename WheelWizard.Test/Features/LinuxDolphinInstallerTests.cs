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
    public void IsDolphinInstalledInFlatpak_ReturnsTrue_WhenFlatpakListHasDolphin()
    {
        _processService.Run("flatpak", "list --app --columns=application", out var stdOut, out _).Returns(Ok(0)).AndDoes(callInfo => callInfo[2] = "Application ID\norg.DolphinEmu.dolphin-emu\n");

        var result = _installer.IsDolphinInstalledInFlatpak();

        Assert.True(result);
    }

    [Fact]
    public void IsDolphinInstalledInFlatpak_ReturnsFalse_WhenFlatpakListHasNoDolphin()
    {
        _processService.Run("flatpak", "list --app --columns=application", out var stdOut, out _).Returns(Ok(0)).AndDoes(callInfo => callInfo[2] = "Application ID\n");

        var result = _installer.IsDolphinInstalledInFlatpak();

        Assert.False(result);
    }

    [Fact]
    public void IsDolphinInstalledInFlatpak_ReturnsFalse_WhenFlatpakListFails()
    {
        _processService.Run("flatpak", "list --app --columns=application", out _, out _).Returns(Ok(1));

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
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenDolphinRemoteCommandFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo")
            .Returns(Ok(1));
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo")
            .Returns(Ok(0));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsFailure);
        Assert.Contains("exit code 1", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenFlathubRemoteCommandFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo")
            .Returns(Ok(1));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsFailure);
        Assert.Contains("exit code 1", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenDolphinInstallCommandFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .RunWithProgressAsync("flatpak", "install --user -y dolphin org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult(Ok(1)));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsFailure);
        Assert.Contains("exit code 1", result.Error.Message);
    }

    [Fact]
    public async Task InstallFlatpakDolphin_ReturnsFailure_WhenWarmupLaunchFails()
    {
        _commandEnvironment.IsCommandAvailable("flatpak").Returns(true);
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .RunWithProgressAsync("flatpak", "install --user -y dolphin org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult(Ok(0)));
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
            .Run("flatpak", "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .Run("flatpak", "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo")
            .Returns(Ok(0));
        _processService
            .RunWithProgressAsync("flatpak", "install --user -y dolphin org.DolphinEmu.dolphin-emu", Arg.Any<IProgress<int>?>())
            .Returns(Task.FromResult(Ok(0)));
        _processService
            .LaunchAndStopAsync("flatpak", "run org.DolphinEmu.dolphin-emu", TimeSpan.FromSeconds(4))
            .Returns(Task.FromResult(Ok()));

        var result = await _installer.InstallFlatpakDolphin();

        Assert.True(result.IsSuccess);
    }
}
