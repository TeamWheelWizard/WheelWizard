using System.IO.Abstractions;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Services;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Downloads;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Launching;

public sealed class MiiChannelLauncher(
    IDownloadService downloads,
    IWiiRemoteConfigurationService wiiRemoteConfiguration,
    IDolphinLaunchService dolphinLaunchService,
    IApplicationDataLocation applicationData,
    IDolphinPaths dolphinPaths,
    IFileSystem fileSystem,
    IRuntimeEnvironment environment
)
{
    private string MiiChannelPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "MiiChannel.wad");

    public async Task LaunchMiiChannel()
    {
        // Check first so a blocked launch does not enable the virtual Wii Remote.
        var preflightResult = await dolphinLaunchService.PreflightDolphinVersionAsync();
        if (preflightResult.IsFailure)
            return;

        wiiRemoteConfiguration.SetVirtualRemoteEnabled(dolphinPaths.ConfigFolderPath, true);
        var miiChannelExists = fileSystem.File.Exists(MiiChannelPath);

        if (!miiChannelExists)
        {
            // TODO: If we do enable this again, we should also add translations support for the text here
            var downloadQuestion = new YesNoWindow()
                .SetMainText("Install MiiChannel?")
                .SetExtraText("Do you want to install the MiiChannel to launch it?");

            if (await downloadQuestion.AwaitAnswer())
            {
                var downloadedFilePath = await downloads.DownloadToLocationAsync(
                    Endpoints.MiiChannelWAD,
                    MiiChannelPath,
                    "Downloading MiiChannel"
                );
                miiChannelExists = !string.IsNullOrWhiteSpace(downloadedFilePath) && fileSystem.File.Exists(MiiChannelPath);
            }
        }

        if (miiChannelExists)
            await dolphinLaunchService.LaunchDolphin(
                $"-b {ShellQuoting.QuoteArgument(fileSystem.Path.GetFullPath(MiiChannelPath), environment.IsWindows)}",
                versionPreflightResult: preflightResult
            );
    }
}
