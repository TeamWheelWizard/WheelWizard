using WheelWizard.DolphinManagement.Abstractions;

namespace WheelWizard.DolphinManagement.Linux;

public sealed class LinuxDolphinInstaller(ILinuxCommandEnvironment commandEnvironment, ILinuxProcessService processService)
    : IDolphinInstaller
{
    private bool IsFlatpakInstalled()
    {
        return commandEnvironment.IsCommandAvailable("flatpak");
    }

    private async Task<OperationResult> InstallFlatpak(IProgress<int>? progress = null)
    {
        if (IsFlatpakInstalled())
            return Ok();

        var packageManagerCommand = commandEnvironment.DetectPackageManagerInstallCommand();
        if (string.IsNullOrWhiteSpace(packageManagerCommand))
            return Fail("Unsupported Linux distribution. Could not detect a package manager command.");

        var installResult = await processService.RunWithProgressAsync("pkexec", $"{packageManagerCommand} flatpak", progress);
        if (installResult.IsFailure)
            return installResult.Error;

        if (installResult.Value is 126 or 127)
            return Fail("You need to be an administrator to install Flatpak.");

        if (installResult.Value != 0)
            return Fail($"Flatpak installation failed with exit code {installResult.Value}.");

        if (!IsFlatpakInstalled())
            return Fail("Flatpak installation completed, but Flatpak is still unavailable.");

        return Ok();
    }

    public async Task<OperationResult> InstallDolphin(IProgress<int>? progress = null)
    {
        if (!IsFlatpakInstalled())
        {
            var installFlatpakResult = await InstallFlatpak(progress);
            if (installFlatpakResult.IsFailure)
                return installFlatpakResult;
        }

        var addRemoteResult = processService.Run(
            "flatpak",
            "remote-add --if-not-exists --user dolphin https://flatpak.dolphin-emu.org/releases.flatpakrepo"
        );
        if (addRemoteResult.IsFailure)
            return addRemoteResult.Error;

        if (addRemoteResult.Value != 0)
            return Fail($"Adding the Dolphin Flatpak remote failed with exit code {addRemoteResult.Value}.");

        addRemoteResult = processService.Run(
            "flatpak",
            "remote-add --if-not-exists --user flathub https://dl.flathub.org/repo/flathub.flatpakrepo"
        );
        if (addRemoteResult.IsFailure)
            return addRemoteResult.Error;

        if (addRemoteResult.Value != 0)
            return Fail($"Adding the Flathub Flatpak remote failed with exit code {addRemoteResult.Value}.");

        var installDolphinResult = await processService.RunWithProgressAsync(
            "flatpak",
            "install --user -y dolphin org.DolphinEmu.dolphin-emu",
            progress
        );
        if (installDolphinResult.IsFailure)
            return installDolphinResult.Error;

        if (installDolphinResult.Value != 0)
            return Fail($"Dolphin installation failed with exit code {installDolphinResult.Value}.");

        var launchResult = await processService.LaunchAndStopAsync("flatpak", "run org.DolphinEmu.dolphin-emu", TimeSpan.FromSeconds(4));
        return launchResult.IsFailure ? launchResult.Error : Ok();
    }
}
