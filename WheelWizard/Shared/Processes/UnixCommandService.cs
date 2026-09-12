namespace WheelWizard.Shared.Processes;

public interface IUnixCommandService
{
    bool IsCommandAvailable(string command);
    string DetectPackageManagerInstallCommand();
}

public sealed class UnixCommandService(IUnixProcessService processes) : IUnixCommandService
{
    public bool IsCommandAvailable(string command)
    {
        var result = processes.Run("/usr/bin/env", new[] { "sh", "-c", "--", $"command -v -- {command}" }, out _, out _);
        return result.IsSuccess && result.Value == 0;
    }

    public string DetectPackageManagerInstallCommand()
    {
        foreach (
            var (command, install) in new[]
            {
                ("apt", "apt install -y"),
                ("apt-get", "apt-get -y install"),
                ("dnf", "dnf -y install"),
                ("yum", "yum -y install"),
                ("pacman", "pacman --noconfirm -S"),
                ("zypper", "zypper --non-interactive install"),
            }
        )
        {
            if (IsCommandAvailable(command))
                return install;
        }
        return string.Empty;
    }
}
