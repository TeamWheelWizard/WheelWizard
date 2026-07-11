using WheelWizard.Helpers;

namespace WheelWizard.DolphinManagent.Linux;

public interface ILinuxCommandEnvironment
{
    bool IsCommandAvailable(string command);
    string DetectPackageManagerInstallCommand();
}

public sealed class LinuxCommandEnvironment : ILinuxCommandEnvironment
{
    public bool IsCommandAvailable(string command)
    {
        return EnvHelper.IsValidUnixCommand(command);
    }

    public string DetectPackageManagerInstallCommand()
    {
        return EnvHelper.DetectLinuxPackageManagerInstallCommand();
    }
}
