using WheelWizard.DolphinManagent.Abstractions;

namespace WheelWizard.DolphinManagent.Linux;

public sealed class LinuxDolphinLocator(ILinuxCommandEnvironment commandEnvironment, ILinuxProcessService processService) : IDolphinLocator
{
    private bool IsDolphinInstalledInFlatpak()
    {
        const string dolphinAppId = "org.DolphinEmu.dolphin-emu";
        var processResult = processService.Run("flatpak", "list --app --columns=application", out var stdOut, out _);

        return processResult.IsSuccess && processResult.Value == 0 && stdOut.Split('\n').Any(line => line == dolphinAppId);
    }

    private bool IsDolphinInstalledNative()
    {
        if (!commandEnvironment.IsCommandAvailable("dolphin-emu"))
        {
            return false;
        }
        var processResult = processService.Run("dolphin-emu", "--version");
        return processResult.IsSuccess && processResult.Value == 0;
    }

    public IReadOnlyList<DolphinInstallation> DetectInstallations()
    {
        return
        [
            new("Flatpak", "flatpak run org.DolphinEmu.dolphin-emu", IsDolphinInstalledInFlatpak()),
            new("Native", "dolphin-emu", IsDolphinInstalledNative()),
        ];
    }
}
