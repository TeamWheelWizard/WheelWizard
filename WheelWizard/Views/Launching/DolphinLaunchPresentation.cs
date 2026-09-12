using WheelWizard.DolphinInstaller;
using WheelWizard.Launching;
using WheelWizard.Views.Popups.Generic;
using Button = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Launching;

public sealed class DolphinLaunchPresentation : IDolphinLaunchPresentation
{
    public async Task ShowUnverifiedVersionAsync() =>
        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText(t("message_warning.dolphin_version_unverified.title"))
            .SetInfoText(t("message_warning.dolphin_version_unverified.extra", DolphinVersion.MinimumDisplayText))
            .ShowDialog();

    public async Task<DolphinVersionAction> ChooseOutdatedVersionActionAsync(string? version)
    {
        var popup = new YesNoWindow()
            .SetButtonVariants(Button.ButtonsVariantType.Primary, Button.ButtonsVariantType.Danger)
            .SetButtonText(t("action.update"), t("action.play_anyway"))
            .SetMainText(t("question.dolphin_outdated.title"))
            .SetExtraText(t("question.dolphin_outdated.extra", version ?? t("state.unknown"), DolphinVersion.MinimumDisplayText));
        if (await popup.AwaitAnswer())
            return DolphinVersionAction.Update;
        return popup.NoButtonClicked ? DolphinVersionAction.PlayAnyway : DolphinVersionAction.Cancel;
    }

    public void OpenUpdateInstructions(bool bundled) =>
        ViewUtils.OpenLink(
            bundled ? "https://flathub.org/apps/io.github.TeamWheelWizard.WheelWizard" : "https://dolphin-emu.org/download/"
        );

    public async Task RunUpdateAsync(Func<IProgress<int>, Task<OperationResult>> update)
    {
        var window = new ProgressWindow(t("progress.updating_dolphin"));
        window.Show();
        OperationResult result;
        try
        {
            result = await update(new Progress<int>(percentage => window.UpdateProgress(percentage)));
        }
        finally
        {
            window.Close();
        }
        if (result.IsSuccess)
            ViewUtils.ShowSnackbar(t("snackbar_success.dolphin_updated"));
        else
        {
            ViewUtils.ShowSnackbar(result.Error.Message, ViewUtils.SnackbarType.Danger);
            OpenUpdateInstructions(false);
        }
    }

    public void ShowLaunchFailure(string reason) =>
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText("Failed to launch Dolphin")
            .SetInfoText($"Reason: {reason}")
            .Show();
}
