using Avalonia.Threading;
using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Settings;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

public class RrBetaLauncher : ILauncher
{
    public string GameTitle { get; } = "Retro Rewind Beta";
    private static string RrLaunchJsonFilePath => PathManager.RrLaunchJsonFilePath;
    private readonly ICustomDistributionSingletonService _customDistributionSingletonService;
    private readonly ISettingsManager _settingsManager;

    public RrBetaLauncher(ICustomDistributionSingletonService customDistributionSingletonService, ISettingsManager settingsManager)
    {
        _customDistributionSingletonService = customDistributionSingletonService;
        _settingsManager = settingsManager;
    }

    public async Task Launch()
    {
        try
        {
            DolphinLaunchHelper.KillDolphin();
            if (WiiMoteSettings.IsForceSettingsEnabled())
                WiiMoteSettings.DisableVirtualWiiMote();
            await ModsLaunchHelper.PrepareModsForLaunch(PathManager.RrBetaMyStuffFolderPath);
            if (!File.Exists(PathManager.GameFilePath))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Warning)
                        .SetTitleText("Invalid game path")
                        .SetInfoText(Phrases.MessageWarning_NotFindGame_Extra)
                        .Show();
                });
                return;
            }

            RetroRewindLaunchHelper.GenerateLaunchJson(PathManager.RrBetaXmlFilePath);
            var dolphinLaunchType = _settingsManager.LaunchWithDolphin.Get() ? "" : "-b";
            DolphinLaunchHelper.LaunchDolphin(
                $"{dolphinLaunchType} -e {EnvHelper.QuotePath(Path.GetFullPath(RrLaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False"
            );
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText("Failed to launch Retro Rewind Beta")
                    .SetInfoText($"Reason: {ex.Message}")
                    .Show();
            });
        }
    }

    public async Task Install()
    {
        var progressWindow = new ProgressWindow("Installing test build");
        progressWindow.Show();
        var installResult = await _customDistributionSingletonService.RetroRewindBeta.InstallAsync(progressWindow);
        progressWindow.Close();
        if (installResult.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Unable to install test build")
                .SetInfoText(installResult.Error.Message)
                .ShowDialog();
        }
    }

    public async Task Update()
    {
        var progressWindow = new ProgressWindow("Updating test build");
        progressWindow.Show();
        await _customDistributionSingletonService.RetroRewindBeta.UpdateAsync(progressWindow);
        progressWindow.Close();
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var statusResult = await _customDistributionSingletonService.RetroRewindBeta.GetCurrentStatusAsync();
        if (statusResult.IsFailure)
            return WheelWizardStatus.NotInstalled;
        return statusResult.Value;
    }
}
