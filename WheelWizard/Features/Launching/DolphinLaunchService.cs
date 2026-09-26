using System.Diagnostics;
using System.IO.Abstractions;
using System.Text.RegularExpressions;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.DolphinInstaller;
using WheelWizard.Settings;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Launching;

public interface IDolphinLaunchService
{
    void KillDolphin();
    Task<OperationResult> PreflightDolphinVersionAsync();
    Task<OperationResult> LaunchDolphin(string arguments = "", bool shellExecute = false, OperationResult? versionPreflightResult = null);
}

public sealed class DolphinLaunchService(
    ISettingsManager settings,
    IDolphinPaths paths,
    ICustomDistributionPaths distributionPaths,
    IFileSystem fileSystem,
    IRuntimeEnvironment environment,
    IProcessLauncher processes,
    ILinuxProcessService linuxProcesses,
    IDolphinVersionService versions,
    ILinuxDolphinInstaller installer,
    IDolphinLaunchPresentation presentation
) : IDolphinLaunchService
{
    private string QuotePath(string path) => ShellQuoting.QuoteArgument(path, environment.IsWindows);

    private bool IsFlatpakSandboxed => paths.Layout.IsFlatpakSandboxed();

    public void KillDolphin() => processes.KillByName(fileSystem.Path.GetFileNameWithoutExtension(paths.ExecutablePath));

    public async Task<OperationResult> PreflightDolphinVersionAsync()
    {
        var (status, version) = await Task.Run(versions.CheckConfiguredDolphin);
        if (status == DolphinVersionStatus.Supported)
            return Ok();
        if (status == DolphinVersionStatus.Unknown)
        {
            await presentation.ShowUnverifiedVersionAsync();
            return Ok();
        }
        var choice = await presentation.ChooseOutdatedVersionActionAsync(version);
        if (choice == DolphinVersionAction.PlayAnyway)
            return Ok();
        if (choice == DolphinVersionAction.Cancel)
            return Fail("Dolphin launch was cancelled.");
        if (IsFlatpakSandboxed || !environment.IsLinux || !paths.Layout.IsFlatpakDolphinFilePath(paths.ExecutablePath))
            presentation.OpenUpdateInstructions(IsFlatpakSandboxed);
        else
        {
            var appId = paths.Layout.ExtractDolphinFlatpakAppId(paths.ExecutablePath);
            await presentation.RunUpdateAsync(progress => installer.UpdateFlatpakDolphin(appId, progress));
        }
        return Fail("Dolphin launch did not proceed because an update was requested.");
    }

    private bool IsFixableFlatpakGamePath(string gameFilePath)
    {
        // Because with the file picker on a Flatpak build, we get XDG portal paths like these...
        // We can fix Flatpak Dolphin to gain access to this game file path though.
        var xdgRuntimeDir = environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty;
        if (!xdgRuntimeDir.StartsWith('/'))
        {
            var fixablePattern = @"^/run/user/(\d+)/doc(?:/|$)";
            var fixablePatternRegex = new Regex(fixablePattern);
            return fixablePatternRegex.IsMatch(gameFilePath);
        }
        else
        {
            var xdgRuntimeDirDocPath = fileSystem.Path.Combine(xdgRuntimeDir, "doc");
            return gameFilePath.Equals(xdgRuntimeDirDocPath, StringComparison.Ordinal)
                || gameFilePath.StartsWith(xdgRuntimeDirDocPath + "/", StringComparison.Ordinal);
        }
    }

    private bool TryFixFlatpakPortalAccess(string path, string dolphinAppId, string additionalFlag = "")
    {
        if (IsFixableFlatpakGamePath(path))
        {
            try
            {
                var result = linuxProcesses.Run(
                    "flatpak",
                    new[]
                    {
                        "document-export",
                        $"--app={dolphinAppId}",
                        string.IsNullOrWhiteSpace(additionalFlag) ? "-r" : additionalFlag,
                        "--",
                        path,
                    },
                    out _,
                    out _
                );
                return result.IsSuccess && result.Value == 0;
            }
            catch
            {
                // Ignore failed export
            }
        }
        return false;
    }

    private string FixFlatpakDolphinPermissions(string flatpakDolphinLocation)
    {
        var dolphinAppId = paths.Layout.ExtractDolphinFlatpakAppId(flatpakDolphinLocation);
        var fixedFlatpakDolphinLocation = flatpakDolphinLocation;
        void AddFilesystemPerm(string newFilesystemPerm, string mode = "")
        {
            var flatpakRunCommand = "flatpak run";
            fixedFlatpakDolphinLocation = fixedFlatpakDolphinLocation.Replace(
                flatpakRunCommand,
                $"{flatpakRunCommand} --filesystem={QuotePath(fileSystem.Path.GetFullPath(newFilesystemPerm))}{mode}"
            );
        }

        // Read-write permissions

        // Try to export all portal-based paths to the Dolphin Flatpak so there are no issues.
        // We are going to try to fix all user-configurable paths (excluding the Dolphin executable).
        if (!TryFixFlatpakPortalAccess(paths.UserFolderPath, dolphinAppId, "-w"))
            AddFilesystemPerm(paths.UserFolderPath, ":rw");
        // It doesn't seem viable to always enforce read-only Riivolution folder access
        // while granting read-write to the save subdirectory,
        // assuming the path is overridden (think a Dolphin user folder inside it...).
        // The Dolphin Flatpak itself would have write access to the entire Riivolution folder
        // anyway in the default configuration, so we will only use read-only permissions on
        // launch files if possible, not folders.
        if (!TryFixFlatpakPortalAccess(distributionPaths.RootFolderPath, dolphinAppId, "-w"))
            AddFilesystemPerm(distributionPaths.RootFolderPath, ":rw");

        // Read-only permissions on files where possible

        if (!TryFixFlatpakPortalAccess(settings.Get<string>(settings.GAME_LOCATION), dolphinAppId, "-r"))
            AddFilesystemPerm(settings.Get<string>(settings.GAME_LOCATION), ":ro");
        // We need to provide the directory where the `RR.json` is located in for portal access!
        if (!TryFixFlatpakPortalAccess(fileSystem.Path.GetDirectoryName(distributionPaths.LaunchJsonFilePath) ?? "", dolphinAppId, "-r"))
            AddFilesystemPerm(distributionPaths.LaunchJsonFilePath, ":ro");

        return fixedFlatpakDolphinLocation;
    }

    // Make sure all file arguments are absolute paths
    public async Task<OperationResult> LaunchDolphin(
        string arguments = "",
        bool shellExecute = false,
        OperationResult? versionPreflightResult = null
    )
    {
        versionPreflightResult ??= await PreflightDolphinVersionAsync();
        if (versionPreflightResult.IsFailure)
            return versionPreflightResult;

        try
        {
            var startInfo = new ProcessStartInfo();

            // The Flatpak sandbox always uses the Dolphin wrapper to launch the bundled Dolphin version with a split config.
            var cannotPassUserFolder = environment.IsLinux && paths.Layout.IsLinuxDolphinConfigSplit();
            var userFolderArgument = cannotPassUserFolder ? "" : $"-u {QuotePath(fileSystem.Path.GetFullPath(paths.UserFolderPath))}";
            var dolphinLaunchArguments = $"{arguments} {userFolderArgument}";

            var dolphinLocation = paths.ExecutablePath;
            if (environment.IsWindows)
            {
                // Windows builds
                startInfo.FileName = fileSystem.Path.GetFullPath(dolphinLocation);
                startInfo.Arguments = dolphinLaunchArguments;
                startInfo.UseShellExecute = shellExecute;
            }
            else
            {
                startInfo.FileName = "/usr/bin/env";
                startInfo.ArgumentList.Add("sh");
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add("--");
                if (environment.IsLinux)
                {
                    if (!IsFlatpakSandboxed && paths.Layout.IsFlatpakDolphinFilePath(dolphinLocation))
                        dolphinLocation = FixFlatpakDolphinPermissions(dolphinLocation);
                    else
                        startInfo.EnvironmentVariables["QT_QPA_PLATFORM"] = "xcb";

                    if (IsFlatpakSandboxed)
                    {
                        // The bundled `dolphin-emu-wrapper` changes XDG_CONFIG_HOME and XDG_DATA_HOME to these folders.
                        // We need to ensure they point to the correct folders before launching Dolphin.
                        fileSystem.EnsureRelativeSymlink(
                            paths.Layout.LinuxFlatpakBundledDolphinXdgConfigDir,
                            paths.ConfigFolderPath,
                            createTarget: true
                        );
                        fileSystem.EnsureRelativeSymlink(paths.Layout.LinuxFlatpakBundledDolphinXdgDataDir, paths.UserFolderPath);
                    }
                }
                startInfo.ArgumentList.Add($"{dolphinLocation} {dolphinLaunchArguments}");
                startInfo.UseShellExecute = false;
            }

            processes.Start(startInfo);
            return Ok();
        }
        catch (Exception ex)
        {
            presentation.ShowLaunchFailure(ex.Message);
            return new OperationError { Message = $"Failed to launch Dolphin: {ex.Message}", Exception = ex };
        }
    }
}
