using WheelWizard.Helpers;
using WheelWizard.Services;
using WheelWizard.Shared.Downloads;
using WheelWizard.Views.Downloads;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Launching;

public sealed class MiiChannelLauncher(
    IDownloadService downloads,
    IWiiRemoteConfigurationService wiiRemoteConfiguration,
    IDolphinLaunchService dolphinLaunchService
)
{
    private static string MiiChannelPath => Path.Combine(PathManager.WheelWizardAppdataPath, "MiiChannel.wad");

    public async Task LaunchMiiChannel()
    {
        // Check first so a blocked launch does not enable the virtual Wii Remote.
        var preflightResult = await dolphinLaunchService.PreflightDolphinVersionAsync();
        if (preflightResult.IsFailure)
            return;

        wiiRemoteConfiguration.SetVirtualRemoteEnabled(PathManager.ConfigFolderPath, true);
        var miiChannelExists = File.Exists(MiiChannelPath);
        ;

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
                //we wait to make sure the file is written to disk
                await Task.Delay(200);
                miiChannelExists = !string.IsNullOrWhiteSpace(downloadedFilePath) && File.Exists(MiiChannelPath);
            }
        }

        if (miiChannelExists)
            await dolphinLaunchService.LaunchDolphin(
                $"-b {EnvHelper.QuotePath(Path.GetFullPath(MiiChannelPath))}",
                versionPreflightResult: preflightResult
            );
    }
}
