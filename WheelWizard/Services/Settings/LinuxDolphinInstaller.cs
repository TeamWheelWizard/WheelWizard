using System.Diagnostics;
using System.Text.RegularExpressions;
using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Settings;

public static class LinuxDolphinInstaller
{
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
        IProgress<int>? progress = null)
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
    public static bool IsFlatPakInstalled()
    {
        return EnvHelper.IsValidUnixCommand("flatpak");
    }

    /// <summary>
    /// Installs Flatpak
    /// Reports progress via the provided IProgress callback.
    /// </summary>
    /// <returns>True if installation succeeded; otherwise, false.</returns>
    public static async Task<bool> InstallFlatpak(IProgress<int>? progress = null)
    {
        if (IsFlatPakInstalled())
            return true;

        // Detect the package manager
        var packageManagerCommand = EnvHelper.DetectLinuxPackageManagerInstallCommand();
        if (string.IsNullOrEmpty(packageManagerCommand))
            return false; // Unsupported distro

        // Install Flatpak
        var exitCode = await RunProcessWithProgressAsync("pkexec", $"{packageManagerCommand} flatpak", progress);
        if (exitCode is 127 or 126) //this is error unauthorized
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                new MessageBoxWindow().SetTitleText("Error")
                    .SetTitleText("You need to be an administrator to install Flatpak").Show();
            });
        }

        return exitCode == 0 && IsFlatPakInstalled();
    }


    /// <summary>
    /// Installs Dolphin via Flatpak.
    /// Ensures that Flatpak is installed and reports progress via the provided IProgress callback.
    /// </summary>
    /// <returns>True if Dolphin was successfully installed; otherwise, false.</returns>
    public static async Task<bool> InstallFlatpakDolphin(IProgress<int>? progress = null)
    {
        // Ensure Flatpak is installed; if not, attempt installation.
        if (!IsFlatPakInstalled())
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
                StartInfo = new()
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
                    .SetTitleText("Failed to launch Dolphin")
                    .SetInfoText(ex.Message).Show();
            });
        }

        return true;
    }
}
