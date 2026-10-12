using WheelWizard.Launching;
using WheelWizard.Views.Dialogs;

namespace WheelWizard.Launching.Presentation;

public sealed class LaunchPrompts : ILaunchPrompts
{
    public Task<bool> ConfirmPatchCleanupAsync() =>
        new YesNoWindow()
            .SetButtonText(t("action.delete"), t("action.keep"))
            .SetMainText(t("question.launch_clear_mods_found.title"))
            .SetExtraText(t("question.launch_clear_patches_found.extra"))
            .AwaitAnswer();
}
