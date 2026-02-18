namespace WheelWizard.DolphinInstaller;

public interface ILinuxDolphinInstaller
{
    bool IsDolphinInstalledInFlatpak();
    bool IsFlatpakInstalled();
    Task<OperationResult> InstallFlatpak(IProgress<int>? progress = null);
    Task<OperationResult> InstallFlatpakDolphin(IProgress<int>? progress = null);
}

public sealed class LinuxDolphinInstaller(ILinuxCommandEnvironment commandEnvironment, ILinuxProcessService processService)
    : ILinuxDolphinInstaller
{
    public bool IsDolphinInstalledInFlatpak()
    {
        var processResult = processService.Run("flatpak", "info org.DolphinEmu.dolphin-emu");
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

        var installDolphinResult = await processService.RunWithProgressAsync(
            "pkexec",
            "flatpak --system install -y org.DolphinEmu.dolphin-emu",
            progress
        );
        if (installDolphinResult.IsFailure)
            return installDolphinResult.Error;

        if (installDolphinResult.Value is 126 or 127)
            return Fail("You need to be an administrator to install Dolphin via Flatpak.");

        if (installDolphinResult.Value != 0)
            return Fail($"Dolphin installation failed with exit code {installDolphinResult.Value}.");

        var launchResult = await processService.LaunchAndStopAsync("flatpak", "run org.DolphinEmu.dolphin-emu", TimeSpan.FromSeconds(4));
        if (launchResult.IsFailure)
            return launchResult.Error;

        return Ok();
    }
}
