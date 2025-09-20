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
        if (Process.GetProcessesByName(Path.GetFileNameWithoutExtension(dolphinLocation)).Length == 0)
            return;

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
            var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty;
            if (EnvHelper.IsRelativeLinuxPath(xdgRuntimeDir))
            {
                var fixablePattern = @"^/run/user/(\d+)/doc";
                var fixablePatternRegex = new Regex(fixablePattern);
                return fixablePatternRegex.IsMatch(gameFilePath);
            }
            else
            {
                var xdgRuntimeDirDocPath = Path.Combine(xdgRuntimeDir, "doc");
                return gameFilePath.StartsWith(xdgRuntimeDirDocPath);
            }
        }
        return false;
    }

    private static bool TryFixFlatpakPortalAccess(string? path, string additionalFlag = "")
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (IsFixableFlatpakGamePath(path))
        {
            try
            {
                Process
                    .Start(
                        new ProcessStartInfo
                        {
                            FileName = "flatpak",
                            ArgumentList =
                            {
                                "document-export",
                                "--app=org.DolphinEmu.dolphin-emu",
                                // Default to a flag that is on by default
                                string.IsNullOrWhiteSpace(additionalFlag)
                                    ? "-r"
                                    : additionalFlag,
                                "--",
                                path,
                            },
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                            UseShellExecute = false,
                        }
                    )
                    ?.WaitForExit();
                return true;
            }
            catch
            {
                // Ignore failed export
            }
        }
        return false;
    }

    private static string FixFlatpakDolphinPermissions(string flatpakDolphinLocation, string? launchFilePath)
    {
        var fixedFlatpakDolphinLocation = flatpakDolphinLocation;
        void AddFilesystemPerm(string newFilesystemPerm, string mode = "")
        {
            if (string.IsNullOrWhiteSpace(newFilesystemPerm))
                return;
            var flatpakRunCommand = "flatpak run";
            fixedFlatpakDolphinLocation = fixedFlatpakDolphinLocation.Replace(
                flatpakRunCommand,
                $"{flatpakRunCommand} --filesystem={EnvHelper.QuotePath(Path.GetFullPath(newFilesystemPerm))}{mode}"
            );
        }

        // Read-write permissions

        // Try to export all portal-based paths to the Dolphin Flatpak so there are no issues.
        // We are going to try to fix all user-configurable paths (excluding the Dolphin executable).
        if (!TryFixFlatpakPortalAccess(PathManager.UserFolderPath, "-w"))
            AddFilesystemPerm(PathManager.UserFolderPath, ":rw");
        // It doesn't seem viable to always enforce read-only Riivolution folder access
        // while granting read-write to the save subdirectory,
        // assuming the path is overridden (think a Dolphin user folder inside it...).
        // The Dolphin Flatpak itself would have write access to the entire Riivolution folder
        // anyway in the default configuration, so we will only use read-only permissions on
        // launch files if possible, not folders.
        if (!TryFixFlatpakPortalAccess(PathManager.RiivolutionWhWzFolderPath, "-w"))
            AddFilesystemPerm(PathManager.RiivolutionWhWzFolderPath, ":rw");

        // Read-only permissions on files where possible

        var launchPath = launchFilePath;
        if (string.IsNullOrWhiteSpace(launchPath))
            launchPath = PathManager.GameFilePath;

        if (!string.IsNullOrWhiteSpace(launchPath))
        {
            string normalizedLaunchPath;
            try
            {
                normalizedLaunchPath = Path.GetFullPath(launchPath);
            }
            catch
            {
                normalizedLaunchPath = launchPath;
            }

            if (!TryFixFlatpakPortalAccess(normalizedLaunchPath, "-r"))
                AddFilesystemPerm(normalizedLaunchPath, ":ro");
        }
        AddFilesystemPerm(PathManager.RrLaunchJsonFilePath, ":ro");

        return fixedFlatpakDolphinLocation;
    }

    // Make sure all file arguments are absolute paths
    public static void LaunchDolphin(string arguments = "", bool shellExecute = false, string? launchFilePath = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo();

            var cannotPassUserFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PathManager.IsLinuxDolphinConfigSplit();
            var userFolderArgument = cannotPassUserFolder ? "" : $"-u {EnvHelper.QuotePath(Path.GetFullPath(PathManager.UserFolderPath))}";
            var dolphinLaunchArguments = $"{arguments} {userFolderArgument}";

            string? normalizedLaunchPath = null;
            var baseLaunchPath = string.IsNullOrWhiteSpace(launchFilePath) ? PathManager.GameFilePath : launchFilePath;
            if (!string.IsNullOrWhiteSpace(baseLaunchPath))
            {
                try
                {
                    normalizedLaunchPath = Path.GetFullPath(baseLaunchPath);
                }
                catch
                {
                    normalizedLaunchPath = baseLaunchPath;
                }
            }

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
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    if (PathManager.IsFlatpakDolphinFilePath())
                        dolphinLocation = FixFlatpakDolphinPermissions(dolphinLocation, normalizedLaunchPath);
                    else
                        startInfo.EnvironmentVariables["QT_QPA_PLATFORM"] = "xcb";
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
