using Avalonia.Threading;
using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

public class RrBetaLauncher : ILauncher
{
    public string GameTitle { get; } = "Retro Rewind Beta";
    private static string RrLaunchJsonFilePath => PathManager.RrLaunchJsonFilePath;

    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } =
        App.Services.GetRequiredService<ICustomDistributionSingletonService>();

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
            var dolphinLaunchType = (bool)SettingsManager.LAUNCH_WITH_DOLPHIN.Get() ? "" : "-b";
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
        var installResult = await CustomDistributionSingletonService.RetroRewindBeta.InstallAsync(progressWindow);
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
        await CustomDistributionSingletonService.RetroRewindBeta.UpdateAsync(progressWindow);
        progressWindow.Close();
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        if (CustomDistributionSingletonService == null)
        {
            return WheelWizardStatus.NotInstalled;
        }
        var statusResult = await CustomDistributionSingletonService.RetroRewindBeta.GetCurrentStatusAsync();
        if (statusResult.IsFailure)
            return WheelWizardStatus.NotInstalled;
        return statusResult.Value;
    }
}
