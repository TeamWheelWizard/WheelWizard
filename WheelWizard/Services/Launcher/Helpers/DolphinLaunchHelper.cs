using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WheelWizard.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher.Helpers;

public static class DolphinLaunchHelper
{
    public static void KillDolphin() //dont tell PETA
    {
        var dolphinLocation = PathManager.DolphinFilePath;
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(dolphinLocation)).Length == 0) return;

        var dolphinProcesses = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(dolphinLocation));
        foreach (var process in dolphinProcesses)
        {
            process.Kill();
        }
    }

    private static bool IsFixableFlatpakGamePath(string gameFilePath)
    {
        if (PathManager.IsFlatpakDolphinFilePath())
        {
            // Because with the file picker on a Flatpak build, we get XDG portal paths like these...
            // We can fix Flatpak Dolphin to gain access to this game file path though.
            string xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty;
            if (EnvHelper.IsRelativeLinuxPath(xdgRuntimeDir))
            {
                string fixablePattern = @"^/run/user/(\d+)/doc";
                Regex fixablePatternRegex = new Regex(fixablePattern);
                return fixablePatternRegex.IsMatch(gameFilePath);
            }
            else
            {
                string xdgRuntimeDirDocPath = Path.Combine(xdgRuntimeDir, "doc");
                return gameFilePath.StartsWith(xdgRuntimeDirDocPath);
            }
        }
        return false;
    }

    private static bool TryFixFlatpakGameFileAccess()
    {
        var gameFilePath = PathManager.GameFilePath;
        if (IsFixableFlatpakGamePath(gameFilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "flatpak",
                    ArgumentList = {
                        "document-export",
                        "--app=org.DolphinEmu.dolphin-emu",
                        "-r",
                        "--",
                        gameFilePath,
                    },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();
                return true;
            }
            catch
            {
                // Ignore failed export
            }
        }
        return false;
    }

    private static string FixFlatpakDolphinPermissions(string flatpakDolphinLocation)
    {
        string fixedFlatpakDolphinLocation = flatpakDolphinLocation;
        var addFilesystemPerm = (string newFilesystemPerm, string mode = "") =>
        {
            string flatpakRunCommand = "flatpak run";
            fixedFlatpakDolphinLocation = fixedFlatpakDolphinLocation.Replace(
                flatpakRunCommand,
                $"{flatpakRunCommand} --filesystem=\"{Path.GetFullPath(newFilesystemPerm)}\"{mode}");
        };
        if (!TryFixFlatpakGameFileAccess())
        {
            addFilesystemPerm(PathManager.GameFilePath, ":ro");
        }
        if (!PathManager.LinuxDolphinFlatpakDataDir.Equals(PathManager.UserFolderPath, StringComparison.Ordinal))
        {
            addFilesystemPerm(PathManager.UserFolderPath, ":rw");
        }
        addFilesystemPerm(PathManager.RrLaunchJsonFilePath, ":ro");
        addFilesystemPerm(PathManager.XmlFilePath, ":ro");
        addFilesystemPerm(PathManager.RiivolutionWhWzFolderPath, ":ro");
        addFilesystemPerm(PathManager.SaveFolderPath, ":rw");
        return fixedFlatpakDolphinLocation;
    }

    // Make sure all file arguments are absolute paths
    public static void LaunchDolphin(string arguments = "", bool shellExecute = false)
    {
        try
        {
            var startInfo = new ProcessStartInfo();

            bool cannotPassUserFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PathManager.IsLinuxDolphinConfigSplit();
            string userFolderArgument = cannotPassUserFolder ? "" : $"-u \"{Path.GetFullPath(PathManager.UserFolderPath)}\"";
            string dolphinLaunchArguments = $"{arguments} {userFolderArgument}";

            var dolphinLocation = (string)SettingsManager.DOLPHIN_LOCATION.Get();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows builds
                startInfo.FileName = Path.GetFullPath(dolphinLocation);
                startInfo.Arguments = dolphinLaunchArguments;
                startInfo.UseShellExecute = shellExecute;
            }
            else
            {
                startInfo.FileName = "/usr/bin/env";
                startInfo.ArgumentList.Add("sh");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("--");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                    PathManager.IsFlatpakDolphinFilePath())
                {
                    dolphinLocation = FixFlatpakDolphinPermissions(dolphinLocation);
                }
                startInfo.ArgumentList.Add($"{dolphinLocation} {dolphinLaunchArguments}");
                startInfo.UseShellExecute = false;
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to launch Dolphin")
                .SetInfoText($"Reason: {ex.Message}")
                .Show();
        }
    }
}
