namespace WheelWizard.DolphinInstaller;

public interface ILinuxDolphinInstaller
{
    bool IsDolphinInstalledInFlatpak();
    bool IsDolphinInstalledNative();
    bool IsFlatpakInstalled();
    Task<OperationResult> InstallFlatpak(IProgress<int>? progress = null);
    Task<OperationResult> InstallFlatpakDolphin(IProgress<int>? progress = null);
    Task<OperationResult> UpdateFlatpakDolphin(string appId, IProgress<int>? progress = null);
}

public sealed class LinuxDolphinInstaller(ILinuxCommandEnvironment commandEnvironment, ILinuxProcessService processService)
    : ILinuxDolphinInstaller
{
    public bool IsDolphinInstalledInFlatpak()
    {
        const string dolphinAppId = "org.DolphinEmu.dolphin-emu";
        var processResult = processService.Run("flatpak", "list --app --columns=application", out var stdOut, out _);

        return processResult.IsSuccess && processResult.Value == 0 && stdOut.Split('\n').Any(line => line == dolphinAppId);
    }

    public bool IsDolphinInstalledNative()
    {
        if (!commandEnvironment.IsCommandAvailable("dolphin-emu"))
        {
            return false;
        }
        var processResult = processService.Run("dolphin-emu", "--version");
        return processResult.IsSuccess && processResult.Value == 0;
    }

    public bool IsFlatpakInstalled()
    {
        return commandEnvironment.IsCommandAvailable("flatpak");
    }

    public async Task<OperationResult> InstallFlatpak(IProgress<int>? progress = null)
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

    public async Task<OperationResult> InstallFlatpakDolphin(IProgress<int>? progress = null)
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

    public async Task<OperationResult> UpdateFlatpakDolphin(string appId, IProgress<int>? progress = null)
    {
        if (!IsFlatpakInstalled())
            return Fail("Flatpak is not available, so Dolphin cannot be updated automatically.");

        // No installation scope flag on purpose: Flatpak resolves whichever installation actually
        // holds the app. A system-wide install may still refuse without elevation, which surfaces
        // as a non-zero exit code and lets the caller fall back to the download page.
        var updateResult = await processService.RunWithProgressAsync("flatpak", $"update -y {appId}", progress);
        if (updateResult.IsFailure)
            return updateResult.Error;

        if (updateResult.Value != 0)
            return Fail($"Dolphin update failed with exit code {updateResult.Value}.");

        return Ok();
    }
}
