using System.IO.Abstractions;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Launching;

public interface IRetroRewindLaunchService
{
    Task<OperationResult> LaunchAsync(bool beta = false);
}

public interface ILaunchPrompts
{
    Task<bool> ConfirmPatchCleanupAsync();
}

public sealed class RetroRewindLaunchService(
    ISettingsManager settings,
    IFileSystem fileSystem,
    IDolphinLaunchService dolphin,
    IWiiRemoteConfigurationService remotes,
    IDolphinPaths dolphinPaths,
    ICustomDistributionPaths paths,
    IModsLaunchService mods,
    IRetroRewindLaunchDescriptor descriptor,
    IRuntimeEnvironment environment,
    ILaunchPrompts prompts
) : IRetroRewindLaunchService
{
    private string QuotePath(string path) => ShellQuoting.QuoteArgument(path, environment.IsWindows);

    public async Task<OperationResult> LaunchAsync(bool beta = false)
    {
        try
        {
            //case SHOULD be impossible since launch button should be disabled
            if (!fileSystem.File.Exists(settings.Get<string>(settings.GAME_LOCATION)))
                return Fail(t("message_warning.not_find_game.extra"));

            // Check first so a blocked launch does not kill Dolphin or prepare patches.
            var preflightResult = await dolphin.PreflightDolphinVersionAsync();
            if (preflightResult.IsFailure)
                return preflightResult.Error;

            dolphin.KillDolphin();
            if (settings.Get<bool>(settings.FORCE_WIIMOTE))
                remotes.SetVirtualRemoteEnabled(dolphinPaths.ConfigFolderPath, false);
            var targetFolderPath = (beta ? paths.BetaPatchesFolderPath : paths.PatchesFolderPath);
            var clearTargetFolder = false;
            if (mods.ShouldAskToClearTargetFolder(targetFolderPath))
            {
                clearTargetFolder = await prompts.ConfirmPatchCleanupAsync();
            }

            var modsLaunchResult = await mods.PrepareModsForLaunch(targetFolderPath, clearTargetFolder);
            if (modsLaunchResult.IsFailure)
                return modsLaunchResult.Error;

            descriptor.GenerateLaunchJson(beta ? paths.BetaXmlFilePath : paths.XmlFilePath);
            var dolphinLaunchType = settings.Get<bool>(settings.LAUNCH_WITH_DOLPHIN) ? "" : "-b";
            var dolphinLaunchResult = await dolphin.LaunchDolphin(
                $"{dolphinLaunchType} -e {QuotePath(fileSystem.Path.GetFullPath(paths.LaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False",
                versionPreflightResult: preflightResult
            );
            if (dolphinLaunchResult.IsFailure)
                return dolphinLaunchResult.Error;

            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError
            {
                Message = $"Failed to launch {(beta ? "Retro Rewind Beta" : "Retro Rewind")}: {ex.Message}",
                Exception = ex,
            };
        }
    }
}
