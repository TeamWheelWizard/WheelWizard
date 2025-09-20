using System.IO;
using Avalonia.Threading;
using WheelWizard.CustomDistributions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.Installation;
using WheelWizard.Services.Launcher.Helpers;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement.GameExtraction;

namespace WheelWizard.Services.Launcher;

public class RrLauncher : ILauncher
{
    public string GameTitle { get; } = "Retro Rewind";
    private static string RrLaunchJsonFilePath => PathManager.RrLaunchJsonFilePath;

    [Inject]
    private ICustomDistributionSingletonService CustomDistributionSingletonService { get; set; } =
        App.Services.GetRequiredService<ICustomDistributionSingletonService>();

    [Inject]
    private IGameFileExtractionService GameFileExtractionService { get; set; } =
        App.Services.GetRequiredService<IGameFileExtractionService>();

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

            var extractionResult = await GameFileExtractionService.EnsureExtractedAsync();
            if (extractionResult.IsFailure)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var errorMessage = extractionResult.Error?.Message ?? "Failed to prepare the game files.";
                    if (
                        extractionResult.Error?.ExtraReplacements is { Length: > 0 } extras
                        && extras[0] is string moreInfo
                        && !string.IsNullOrWhiteSpace(moreInfo)
                    )
                        errorMessage += $"\n{moreInfo}";

                    new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Unable to extract game files")
                        .SetInfoText(errorMessage)
                        .Show();
                });
                return;
            }

            var launchFilePath = Path.GetFullPath(extractionResult.Value);

            RetroRewindLaunchHelper.GenerateLaunchJson(launchFilePath);
            var dolphinLaunchType = (bool)SettingsManager.LAUNCH_WITH_DOLPHIN.Get() ? "" : "-b";
            DolphinLaunchHelper.LaunchDolphin(
                $"{dolphinLaunchType} -e {EnvHelper.QuotePath(Path.GetFullPath(RrLaunchJsonFilePath))} --config=Dolphin.Core.EnableCheats=False --config=Achievements.Achievements.Enabled=False",
                launchFilePath: launchFilePath
            );
        }
        catch (Exception ex)
        {
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
        var installResult = await CustomDistributionSingletonService.RetroRewind.InstallAsync(progressWindow);
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
        await CustomDistributionSingletonService.RetroRewind.UpdateAsync(progressWindow);
        progressWindow.Close();
    }

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        if (CustomDistributionSingletonService == null)
        {
            return WheelWizardStatus.NotInstalled;
        }
        var statusResult = await CustomDistributionSingletonService.RetroRewind.GetCurrentStatusAsync();
        if (statusResult.IsFailure)
            return WheelWizardStatus.NotInstalled;
        return statusResult.Value;
    }
}
