using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using WheelWizard.Helpers;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Settings;

public static class LinuxDolphinInstaller
{
    public static bool IsValidCommand(string command)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "/bin/sh",
                Arguments = $"-c \"command -v {command.Split(' ').First()}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDolphinInstalledInFlatpak()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "flatpak",
                Arguments = "info org.DolphinEmu.dolphin-emu",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processInfo);
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Helper method to run a process asynchronously with progress reporting.
    private static async Task<int> RunProcessWithProgressAsync(string fileName, string arguments,
        IProgress<int> progress = null)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(processInfo);

        // Listen for output data to parse progress.
        process.OutputDataReceived += (sender, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var match = Regex.Match(e.Data, @"(\d+)%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
            {
                progress?.Report(percent);
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            var match = Regex.Match(e.Data, @"(\d+)%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int percent))
            {
                progress?.Report(percent);
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Checks if Flatpak is installed by verifying the command exists.
    /// </summary>
    /// <returns>True if Flatpak is available; otherwise, false.</returns>
    public static bool isFlatPakInstalled()
    {
        return IsValidCommand("flatpak");
    }

    /// <summary>
    /// Installs Flatpak
    /// Reports progress via the provided IProgress callback.
    /// </summary>
    /// <returns>True if installation succeeded; otherwise, false.</returns>
    public static async Task<bool> InstallFlatpak(IProgress<int> progress = null)
    {
        if (isFlatPakInstalled())
            return true;

        // Detect the package manager
        var packageManager = EnvHelper.DetectLinuxPackageManager();
        if (string.IsNullOrEmpty(packageManager))
            return false; // Unsupported distro

        // Install Flatpak
        var exitCode = await RunProcessWithProgressAsync("pkexec", $"{packageManager} install -y flatpak", progress);
        if (exitCode is 127 or 126) //this is error unauthorized
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                new MessageBoxWindow().SetTitleText("Error")
                    .SetTitleText("You need to be an administrator to install Flatpak").Show();
            });
        }

        return exitCode == 0 && isFlatPakInstalled();
    }



    /// <summary>
    /// Installs Dolphin via Flatpak.
    /// Ensures that Flatpak is installed and reports progress via the provided IProgress callback.
    /// </summary>
    /// <returns>True if Dolphin was successfully installed; otherwise, false.</returns>
    public static async Task<bool> InstallFlatpakDolphin(IProgress<int> progress = null)
    {
        // Ensure Flatpak is installed; if not, attempt installation.
        if (!isFlatPakInstalled())
        {
            var flatpakInstalled = await InstallFlatpak(progress);
            if (!flatpakInstalled)
                return false;
        }

        // Install Dolphin using Flatpak and report progress.
        var exitCode = await RunProcessWithProgressAsync("pkexec", "flatpak --system install -y org.DolphinEmu.dolphin-emu", progress);
        if (exitCode is 127 or 126) //this is error unauthorized
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                new MessageBoxWindow().SetTitleText("Error")
                    .SetTitleText("You need to be an administrator to install Dolphin via Flatpak").Show();
            });
            return false;
        }

        if (exitCode != 0)
            return false;
        try
        {
            var dolphinProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "flatpak",
                    Arguments = "run org.DolphinEmu.dolphin-emu",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            dolphinProcess.Start();
            await Task.Delay(4000);
            dolphinProcess.Kill();
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                new MessageBoxWindow().SetTitleText("Error")
                    .SetTitleText($"Failed to launch Dolphin")
                    .SetInfoText(ex.Message).Show();
            });
        }

        return true;
    }
    
    
    //this should return null if not found since functions above require it
    public static string? TryFindUserFolderPath()
    {
        // Get the user's home directory.
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
            return null;

        // First, try the default Flatpak location.
        var flatpakUserFolder = Path.Combine(home, ".var", "app", "org.DolphinEmu.dolphin-emu");
        if (Directory.Exists(flatpakUserFolder))
            return flatpakUserFolder;

        // Next, check if there's an override provided via FLATPAK_USER_DIR.
        var flatpakOverride = Environment.GetEnvironmentVariable("FLATPAK_USER_DIR");
        if (!string.IsNullOrEmpty(flatpakOverride))
        {
            // Often the structure remains the same.
            flatpakUserFolder = Path.Combine(flatpakOverride, "org.DolphinEmu.dolphin-emu");
            if (Directory.Exists(flatpakUserFolder))
                return flatpakUserFolder;
        }

        var manualInstall = Path.Combine(home, ".dolphin-emu");
        if (Directory.Exists(manualInstall))
            return manualInstall;

        // If not found, return null.
        return null;
    }
}
