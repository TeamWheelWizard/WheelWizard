using Avalonia.Threading;
using WheelWizard.AutoUpdating;
using WheelWizard.Shared.Downloads;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Updating;

public sealed class UpdatePresentation : IUpdatePresentation
{
    public async Task<bool> ConfirmUpdateAsync(string latestVersion, string currentVersion) =>
        await Dispatcher.UIThread.InvokeAsync(
            () =>
                new YesNoWindow()
                    .SetButtonText(t("action.update"), t("action.maybe_later"))
                    .SetMainText(t("question.new_version_wh_wz.title"))
                    .SetExtraText(t("question.new_version_wh_wz.extra", latestVersion, currentVersion)!)
                    .AwaitAnswer()
        );

    public async Task<bool> ConfirmElevationAsync() =>
        await Dispatcher.UIThread.InvokeAsync(
            () =>
                new YesNoWindow().SetMainText(t("question.update_admin.title")).SetExtraText(t("question.update_admin.extra")).AwaitAnswer()
        );

    public async Task ShowCheckFailureAsync(string message) =>
        await Dispatcher.UIThread.InvokeAsync(
            () =>
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText("Failed to check for updates")
                    .SetInfoText("An error occurred while checking for updates. Please try again later.\nError: " + message)
                    .ShowDialog()
        );

    public async Task ShowUpdateFailureAsync(string message) =>
        await Dispatcher.UIThread.InvokeAsync(
            () =>
                new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Warning)
                    .SetTitleText("Unable to update Wheel Wizard")
                    .SetInfoText(message)
                    .ShowDialog()
        );

    public async Task ShowManualUpdateAsync(string latestVersion, string currentVersion) =>
        await Dispatcher.UIThread.InvokeAsync(
            () =>
                new MessageBoxWindow()
                    .SetTitleText("New Wheel Wizard version")
                    .SetInfoText(
                        $"There is a new Wheel Wizard version available!\nVersion {latestVersion} (You are currently on {currentVersion})\n"
                            + "You can manually update it by going to the GitHub releases at: https://github.com/TeamWheelWizard/WheelWizard/releases"
                    )
                    .Show()
        );

    public async Task<OperationResult> RunUpdateAsync(
        Func<IProgress<DownloadProgress>, CancellationToken, Task<OperationResult>> operation
    ) =>
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var cancellation = new CancellationTokenSource();
            ProgressWindow? window = null;
            var finished = false;
            var progress = new Progress<DownloadProgress>(update =>
            {
                if (finished)
                    return;
                if (window is null)
                {
                    window = new ProgressWindow(t("progress.update_wh_wz"))
                        .SetExtraText(t("progress.latest_wh_wz_github"))
                        .SetCancellationTokenSource(cancellation);
                    window.Show();
                }
                if (update.Retrying)
                    window.SetExtraText($"Retrying... Attempt {update.Attempt}");
                window.SetGoal((update.TotalBytes ?? -1) / (1024d * 1024d));
                window.UpdateProgress(update.Percentage);
            });
            try
            {
                return await operation(progress, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return Ok();
            }
            finally
            {
                finished = true;
                window?.SetCancellationTokenSource(null);
                window?.Close();
            }
        });
}
