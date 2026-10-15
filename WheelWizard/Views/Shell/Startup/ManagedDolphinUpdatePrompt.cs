using Microsoft.Extensions.Logging;
using WheelWizard.DolphinInstaller;
using WheelWizard.Views.Dialogs;

namespace WheelWizard.Views.Shell.Startup;

public interface IManagedDolphinUpdatePrompt
{
    /// <summary>Asks once per start whether to update the Dolphin Wheel Wizard manages; never updates on its own.</summary>
    Task OfferUpdateAsync(CancellationToken cancellationToken = default);
}

public sealed class ManagedDolphinUpdatePrompt(IManagedDolphinService managedDolphin, ILogger<ManagedDolphinUpdatePrompt> logger)
    : IManagedDolphinUpdatePrompt
{
    public async Task OfferUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var release = await managedDolphin.CheckForUpdateAsync(cancellationToken);
            if (release is null)
                return;

            var update = await new YesNoWindow()
                .SetMainText(t("onboarding.dolphin_update_title"))
                .SetExtraText(t("onboarding.dolphin_update_body", new { version = release.ShortRevision }))
                .SetButtonText(t("onboarding.dolphin_update_action"), t("onboarding.dolphin_update_later"))
                .AwaitAnswer();
            if (!update)
                return;

            var window = new ProgressWindow(t("onboarding.dolphin_updating"));
            OperationResult result;
            try
            {
                window.Show();
                var progress = new Progress<ManagedDolphinProgress>(value =>
                {
                    window.SetExtraText(value.Message);
                    window.UpdateProgress(value.Percent);
                });
                result = await managedDolphin.UpdateAsync(progress, cancellationToken);
            }
            finally
            {
                window.Close();
            }

            if (result.IsFailure)
            {
                await new MessageBoxWindow()
                    .SetMessageType(MessageBoxWindow.MessageType.Error)
                    .SetTitleText(t("onboarding.dolphin_update_title"))
                    .SetInfoText(result.Error.Message)
                    .ShowDialog();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Checking for a managed Dolphin update failed");
        }
    }
}
