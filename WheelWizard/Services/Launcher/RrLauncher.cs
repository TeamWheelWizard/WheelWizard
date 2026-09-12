using System.IO.Abstractions;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Launching;
using WheelWizard.Models.Enums;
using WheelWizard.Mods;
using WheelWizard.Settings;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.Controllers;

namespace WheelWizard.Services.Launcher;

public class RrLauncher : ILauncher
{
    private readonly IDolphinLaunchService _dolphinLaunchService;
    public string GameTitle { get; } = "Retro Rewind";
    private string RrLaunchJsonFilePath => _distributionPaths.LaunchJsonFilePath;
    private readonly IFileSystem _fileSystem;
    private readonly IDolphinPaths _dolphinPaths;
    private readonly ICustomDistributionPaths _distributionPaths;
    private readonly IRetroRewindLaunchDescriptor _descriptor;
    private readonly IRuntimeEnvironment _environment;

    private string QuotePath(string path) => ShellQuoting.QuoteArgument(path, _environment.IsWindows);

    private readonly ICustomDistributionSingletonService _customDistributionSingletonService;
    private readonly IModsLaunchService _modsLaunchService;
    private readonly ISettingsManager _settingsManager;
    private readonly IWiiRemoteConfigurationService _wiiRemoteConfiguration;

    public RrLauncher(
        ICustomDistributionSingletonService customDistributionSingletonService,
        IModsLaunchService modsLaunchService,
        ISettingsManager settingsManager,
        IWiiRemoteConfigurationService wiiRemoteConfiguration,
        IDolphinLaunchService dolphinLaunchService,
        IFileSystem fileSystem,
        IDolphinPaths dolphinPaths,
        ICustomDistributionPaths distributionPaths,
        IRetroRewindLaunchDescriptor descriptor,
        IRuntimeEnvironment environment
    )
    {
        _customDistributionSingletonService = customDistributionSingletonService;
        _modsLaunchService = modsLaunchService;
        _settingsManager = settingsManager;
        _wiiRemoteConfiguration = wiiRemoteConfiguration;
        _dolphinLaunchService = dolphinLaunchService;
        _fileSystem = fileSystem;
        _dolphinPaths = dolphinPaths;
        _distributionPaths = distributionPaths;
        _descriptor = descriptor;
        _environment = environment;
    }

    public async Task<OperationResult> Launch()
    {
        try
        {
            //case SHOULD be impossible since launch button should be disabled
            if (!_fileSystem.File.Exists(_settingsManager.Get<string>(_settingsManager.GAME_LOCATION)))
                return Fail(t("message_warning.not_find_game.extra"));

            // Check first so a blocked launch does not kill Dolphin or prepare patches.
            var preflightResult = await _dolphinLaunchService.PreflightDolphinVersionAsync();
            if (preflightResult.IsFailure)
                return preflightResult.Error;

            _dolphinLaunchService.KillDolphin();
            if (_settingsManager.Get<bool>(_settingsManager.FORCE_WIIMOTE))
                _wiiRemoteConfiguration.SetVirtualRemoteEnabled(_dolphinPaths.ConfigFolderPath, false);
            var targetFolderPath = _distributionPaths.PatchesFolderPath;
            var clearTargetFolder = false;
            if (_modsLaunchService.ShouldAskToClearTargetFolder(targetFolderPath))
            {
                clearTargetFolder = await new YesNoWindow()
                    .SetButtonText(t("action.delete"), t("action.keep"))
                    .SetMainText(t("question.launch_clear_mods_found.title"))
                    .SetExtraText(t("question.launch_clear_patches_found.extra"))
                    .AwaitAnswer();
            }

            var modsLaunchResult = await _modsLaunchService.PrepareModsForLaunch(targetFolderPath, clearTargetFolder);
            if (modsLaunchResult.IsFailure)
                return modsLaunchResult.Error;

            _descriptor.GenerateLaunchJson();
            var dolphinLaunchType = _settingsManager.Get<bool>(_settingsManager.LAUNCH_WITH_DOLPHIN) ? "" : "-b";
            var dolphinLaunchResult = await _dolphinLaunchService.LaunchDolphin(
                $"{dolphinLaunchType} -e {QuotePath(_fileSystem.Path.GetFullPath(RrLaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False",
                versionPreflightResult: preflightResult
            );
            if (dolphinLaunchResult.IsFailure)
                return dolphinLaunchResult.Error;

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
