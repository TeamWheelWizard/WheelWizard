using WheelWizard.CustomDistributions;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Distributions;

public sealed class DistributionPrompts : IDistributionPrompts
{
    public Task<bool> ConfirmOldSaveBackupAsync() =>
        new YesNoWindow().SetMainText(t("question.old_rksys_found.title")).SetExtraText(t("question.old_rksys_found.extra")).AwaitAnswer();

    public Task<string?> RequestBetaPasswordAsync() =>
        new TextInputWindow()
            .SetMainText("Please enter Password")
            .SetPlaceholderText("Password")
            .SetButtonText("Cancel", "Submit")
            .ShowDialog();

    public Task<bool> ConfirmPasswordRetryAsync() =>
        new YesNoWindow()
            .SetMainText("Incorrect password")
            .SetExtraText("Do you want to try again?")
            .SetButtonText("Retry", "Cancel")
            .AwaitAnswer();
}
