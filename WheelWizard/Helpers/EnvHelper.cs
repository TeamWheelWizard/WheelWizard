using System.Diagnostics;

namespace WheelWizard.Helpers;

public static class EnvHelper
{
    public static bool IsValidUnixCommand(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/env",
                ArgumentList = { "sh", "-c", "--", $"command -v -- {command}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static string DetectLinuxPackageManagerInstallCommand()
    {
        if (IsValidUnixCommand("apt"))
            return "apt install -y";
        if (IsValidUnixCommand("apt-get"))
            return "apt-get -y install";
        if (IsValidUnixCommand("dnf"))
            return "dnf -y install";
        if (IsValidUnixCommand("yum"))
            return "yum -y install";
        if (IsValidUnixCommand("pacman"))
            return "pacman --noconfirm -S";
        return IsValidUnixCommand("zypper") ? "zypper --non-interactive install" : string.Empty; // Unknown package manager
    }

    public static bool IsFlatpakSandboxed()
    {
        return OperatingSystem.IsLinux()
            && File.Exists("/.flatpak-info")
            && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLATPAK_ID"));
    }

    public static string? MaybeDolphinLocationOverride()
    {
        if (IsFlatpakSandboxed())
        {
            return "/app/bin/dolphin-emu-wrapper";
        }
        return null;
    }

    public static bool IsRelativeLinuxPath(string path)
    {
        return !path.StartsWith('/');
    }

    public static string? NullIfRelativeLinuxPath(string path)
    {
        return IsRelativeLinuxPath(path) ? null : path;
    }

    public static string SingleQuotePath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // PowerShell expects doubled single quotes inside the quoted string
            return $"'{path.Replace("'", "''")}'";
        }
        return $"'{path.Replace("'", "'\\''")}'";
    }

    public static string QuotePath(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Use double quotes outside of PowerShell
            return $"\"{path}\"";
        }
        return SingleQuotePath(path);
    }
}
