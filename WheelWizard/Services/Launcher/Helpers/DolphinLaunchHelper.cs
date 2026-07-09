using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WheelWizard.Helpers;
using WheelWizard.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher.Helpers;

public static class DolphinLaunchHelper
{
    private static ISettingsManager Settings => SettingsRuntime.Current;

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

    private static bool TryFixFlatpakPortalAccess(string path, string dolphinAppId, string additionalFlag = "")
    {
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
                                $"--app={dolphinAppId}",
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

    private static string ExtractDolphinAppId(string flatpakDolphinLocation)
    {
        var defaultAppId = "org.DolphinEmu.dolphin-emu";

        if (string.IsNullOrWhiteSpace(flatpakDolphinLocation))
        {
            return defaultAppId;
        }

        var matches = Regex.Matches(flatpakDolphinLocation, @"(?i)\b[a-z][a-z0-9]*(?:\.[a-z_][a-z0-9_]*){1,}\.[a-z_][a-z0-9_-]*\b");
        return matches.Count == 0 ? defaultAppId : matches[^1].Value;
    }

    private static string FixFlatpakDolphinPermissions(string flatpakDolphinLocation)
    {
        var dolphinAppId = ExtractDolphinAppId(flatpakDolphinLocation);
        var fixedFlatpakDolphinLocation = flatpakDolphinLocation;
        void AddFilesystemPerm(string newFilesystemPerm, string mode = "")
        {
            var flatpakRunCommand = "flatpak run";
            fixedFlatpakDolphinLocation = fixedFlatpakDolphinLocation.Replace(
                flatpakRunCommand,
                $"{flatpakRunCommand} --filesystem={EnvHelper.QuotePath(Path.GetFullPath(newFilesystemPerm))}{mode}"
            );
        }

        // Read-write permissions

        // Try to export all portal-based paths to the Dolphin Flatpak so there are no issues.
        // We are going to try to fix all user-configurable paths (excluding the Dolphin executable).
        if (!TryFixFlatpakPortalAccess(PathManager.UserFolderPath, dolphinAppId, "-w"))
            AddFilesystemPerm(PathManager.UserFolderPath, ":rw");
        // It doesn't seem viable to always enforce read-only Riivolution folder access
        // while granting read-write to the save subdirectory,
        // assuming the path is overridden (think a Dolphin user folder inside it...).
        // The Dolphin Flatpak itself would have write access to the entire Riivolution folder
        // anyway in the default configuration, so we will only use read-only permissions on
        // launch files if possible, not folders.
        if (!TryFixFlatpakPortalAccess(PathManager.RiivolutionWhWzFolderPath, dolphinAppId, "-w"))
            AddFilesystemPerm(PathManager.RiivolutionWhWzFolderPath, ":rw");

        // Read-only permissions on files where possible

        if (!TryFixFlatpakPortalAccess(PathManager.GameFilePath, dolphinAppId, "-r"))
            AddFilesystemPerm(PathManager.GameFilePath, ":ro");
        // We need to provide the directory where the `RR.json` is located in for portal access!
        if (!TryFixFlatpakPortalAccess(Path.GetDirectoryName(PathManager.RrLaunchJsonFilePath) ?? "", dolphinAppId, "-r"))
            AddFilesystemPerm(PathManager.RrLaunchJsonFilePath, ":ro");

        return fixedFlatpakDolphinLocation;
    }

    // Make sure all file arguments are absolute paths
    public static void LaunchDolphin(string arguments = "", bool shellExecute = false)
    {
        try
        {
            var startInfo = new ProcessStartInfo();

            var cannotPassUserFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && PathManager.IsLinuxDolphinConfigSplit();
            var userFolderArgument = cannotPassUserFolder ? "" : $"-u {EnvHelper.QuotePath(Path.GetFullPath(PathManager.UserFolderPath))}";
            var dolphinLaunchArguments = $"{arguments} {userFolderArgument}";

            var dolphinLocation = Settings.Get<string>(Settings.DOLPHIN_LOCATION);
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
                    if (PathManager.IsFlatpakDolphinFilePath(dolphinLocation))
                        dolphinLocation = FixFlatpakDolphinPermissions(dolphinLocation);
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
