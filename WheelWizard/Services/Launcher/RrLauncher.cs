using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Installation;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

public class RrLauncher : ILauncher
{
    public string GameTitle { get; } = "RetroRewind";
    private static string RrLaunchJsonFilePath => Path.Combine(PathManager.WheelWizardAppdataPath, "RR.json");

    public async Task Launch()
    {
        try
        {
            DolphinLaunchHelper.KillDolphin();
            if (WiiMoteSettings.IsForceSettingsEnabled())
                WiiMoteSettings.DisableVirtualWiiMote();
            await ModsLaunchHelper.PrepareModsForLaunch();
            if (!File.Exists(PathManager.GameFilePath))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Warning)
                        .SetTitleText("Invalid game path")
                        .SetInfoText(Phrases.PopupText_NotFindGame).Show();
                });
                return;
            }

            RetroRewindLaunchHelper.GenerateLaunchJson();
            var dolphinLaunchType = (bool)SettingsManager.LAUNCH_WITH_DOLPHIN.Get() ? "" : "-b";
            DolphinLaunchHelper.LaunchDolphin(
                $"{dolphinLaunchType} -e '{RrLaunchJsonFilePath}' --config=Dolphin.Core.EnableCheats=False"
                );
        }
        catch (Exception ex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText("Failed to launch Retro Rewind")
                    .SetInfoText($"Reason: {ex.Message}").Show();
            });
        }
    }

    public Task Install() => RetroRewindInstaller.InstallRetroRewind();
    public Task Update() => RetroRewindUpdater.UpdateRR();

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        if (!SettingsHelper.PathsSetupCorrectly())
            return WheelWizardStatus.ConfigNotFinished;

        var serverEnabled = await HttpClientHelper.GetAsync<string>(Endpoints.RRUrl);
        var rrInstalled = RetroRewindInstaller.IsRetroRewindInstalled();

        if (!serverEnabled.Succeeded)
            return rrInstalled ? WheelWizardStatus.NoServerButInstalled : WheelWizardStatus.NoServer;

        if (!rrInstalled) return WheelWizardStatus.NotInstalled;

        var retroRewindUpToDate = await RetroRewindUpdater.IsRRUpToDate(RetroRewindInstaller.CurrentRRVersion());
        return !retroRewindUpToDate ? WheelWizardStatus.OutOfDate : WheelWizardStatus.Ready;
    }
}
