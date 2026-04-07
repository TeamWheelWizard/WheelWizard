using Avalonia.Threading;
using Serilog;
using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Installation;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Settings;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

public class RrLauncher : ILauncher
{
    public string GameTitle { get; } = "Retro Rewind";
    private static string RrLaunchJsonFilePath => PathManager.RrLaunchJsonFilePath;
    private readonly ICustomDistributionSingletonService _customDistributionSingletonService;
    private readonly ISettingsManager _settingsManager;

    public RrLauncher(ICustomDistributionSingletonService customDistributionSingletonService, ISettingsManager settingsManager)
    {
        _customDistributionSingletonService = customDistributionSingletonService;
        _settingsManager = settingsManager;
    }

    public async Task Launch()
    {
        try
        {
            try
            {
                DolphinLaunchHelper.KillDolphin();
            }
            catch (Exception ex)
            {
                Log.Warning(
                    ex,
                    "Failed to stop Dolphin before launching Retro Rewind. Continuing launch. Dolphin path: {DolphinPath}",
                    PathManager.DolphinFilePath
                );
            }

            if (WiiMoteSettings.IsForceSettingsEnabled())
                WiiMoteSettings.DisableVirtualWiiMote();

            try
            {
                await ModsLaunchHelper.PrepareModsForLaunch();
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Failed to prepare Retro Rewind mods for launch. Mods path: {ModsPath}, MyStuff path: {MyStuffPath}",
                    PathManager.ModsFolderPath,
                    PathManager.MyStuffFolderPath
                );
                throw new InvalidOperationException("Failed to prepare mods for launch.", ex);
            }

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

            try
            {
                RetroRewindLaunchHelper.GenerateLaunchJson();
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Failed to generate Retro Rewind launch json. Json path: {LaunchJsonPath}, Xml path: {XmlPath}",
                    PathManager.RrLaunchJsonFilePath,
                    PathManager.XmlFilePath
                );
                throw new InvalidOperationException("Failed to generate the Retro Rewind launch config.", ex);
            }

            var dolphinLaunchType = _settingsManager.Get<bool>(_settingsManager.LAUNCH_WITH_DOLPHIN) ? "" : "-b";
            try
            {
                DolphinLaunchHelper.LaunchDolphin(
                    $"{dolphinLaunchType} -e {EnvHelper.QuotePath(Path.GetFullPath(RrLaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False",
                    throwOnFailure: true
                );
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Failed to start Dolphin for Retro Rewind. Dolphin path: {DolphinPath}, Game path: {GamePath}, User path: {UserPath}, Launch json: {LaunchJsonPath}",
                    PathManager.DolphinFilePath,
                    PathManager.GameFilePath,
                    PathManager.UserFolderPath,
                    PathManager.RrLaunchJsonFilePath
                );
                throw new InvalidOperationException("Failed to start Dolphin with the Retro Rewind launch config.", ex);
            }
        }
        catch (Exception ex)
        {
            if (ex.InnerException is null)
                Log.Error(ex, "Failed to launch Retro Rewind");

            Dispatcher.UIThread.Post(() =>
            {
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText("Failed to launch Retro Rewind")
                    .SetInfoText($"Reason: {ex.Message}")
                    .Show();
            });
        }
    }

    public async Task Install()
    {
        var progressWindow = new ProgressWindow();
        progressWindow.Show();
        var installResult = await _customDistributionSingletonService.RetroRewind.InstallAsync(progressWindow);
        progressWindow.Close();
        if (installResult.IsFailure)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Unable to install RetroRewind")
                .SetInfoText(installResult.Error.Message)
                .ShowDialog();
        }
    }

    public async Task Update()
    {
        var progressWindow = new ProgressWindow();
        progressWindow.Show();
        await _customDistributionSingletonService.RetroRewind.UpdateAsync(progressWindow);
        progressWindow.Close();
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var statusResult = await _customDistributionSingletonService.RetroRewind.GetCurrentStatusAsync();
        if (statusResult.IsFailure)
            return WheelWizardStatus.NotInstalled;
        return statusResult.Value;
    }
}
