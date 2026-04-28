using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Mods;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Settings;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

public class RrLauncher : ILauncher
{
    public string GameTitle { get; } = "Retro Rewind";
    private static string RrLaunchJsonFilePath => PathManager.RrLaunchJsonFilePath;
    private readonly ICustomDistributionSingletonService _customDistributionSingletonService;
    private readonly IModsLaunchService _modsLaunchService;
    private readonly ISettingsManager _settingsManager;

    public RrLauncher(
        ICustomDistributionSingletonService customDistributionSingletonService,
        IModsLaunchService modsLaunchService,
        ISettingsManager settingsManager
    )
    {
        _customDistributionSingletonService = customDistributionSingletonService;
        _modsLaunchService = modsLaunchService;
        _settingsManager = settingsManager;
    }

    public async Task<OperationResult> Launch()
    {
        try
        {
            //case SHOULD be impossible since launch button should be disabled
            if (!File.Exists(PathManager.GameFilePath))
                return Fail(Phrases.MessageWarning_NotFindGame_Extra);
            
            DolphinLaunchHelper.KillDolphin();
            if (WiiMoteSettings.IsForceSettingsEnabled())
                WiiMoteSettings.DisableVirtualWiiMote();
            var targetFolderPath = PathManager.PatchesFolderPath;
            var clearTargetFolder = false;
            if (_modsLaunchService.ShouldAskToClearTargetFolder(targetFolderPath))
            {
                clearTargetFolder = await new YesNoWindow()
                    .SetButtonText(Common.Action_Delete, Common.Action_Keep)
                    .SetMainText(Phrases.Question_LaunchClearModsFound_Title)
                    .SetExtraText("You are about to launch the game without mods. Do you want to clear your Patches folder?")
                    .AwaitAnswer();
            }

            var modsLaunchResult = await _modsLaunchService.PrepareModsForLaunch(targetFolderPath, clearTargetFolder);
            if (modsLaunchResult.IsFailure)
                return modsLaunchResult.Error;

            RetroRewindLaunchHelper.GenerateLaunchJson();
            var dolphinLaunchType = _settingsManager.Get<bool>(_settingsManager.LAUNCH_WITH_DOLPHIN) ? "" : "-b";
            DolphinLaunchHelper.LaunchDolphin(
                $"{dolphinLaunchType} -e {EnvHelper.QuotePath(Path.GetFullPath(RrLaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False"
            );
            return Ok();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to launch Retro Rewind: {ex.Message}", Exception = ex };
        }
    }

    public async Task<OperationResult> Install()
    {
        var progressWindow = new ProgressWindow();
        try
        {
            progressWindow.Show();
            return await _customDistributionSingletonService.RetroRewind.InstallAsync(progressWindow);
        }
        finally
        {
            progressWindow.Close();
        }
    }

    public async Task<OperationResult> Update()
    {
        var progressWindow = new ProgressWindow();
        try
        {
            progressWindow.Show();
            return await _customDistributionSingletonService.RetroRewind.UpdateAsync(progressWindow);
        }
        finally
        {
            progressWindow.Close();
        }
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var statusResult = await _customDistributionSingletonService.RetroRewind.GetCurrentStatusAsync();
        if (statusResult.IsFailure)
            return WheelWizardStatus.NotInstalled;
        return statusResult.Value;
    }
}
