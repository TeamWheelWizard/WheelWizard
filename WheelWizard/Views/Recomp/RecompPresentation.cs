using WheelWizard.Recomp;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Recomp;

public sealed class RecompPresentation : IRecompPresentation
{
    public Task<bool> ConfirmUseDolphinDataAsync() =>
        new YesNoWindow()
            .SetMainText(t("question.recomp_use_dolphin_nand.title"))
            .SetExtraText(t("question.recomp_use_dolphin_nand.extra"))
            .AwaitAnswer();

    public Task<bool> ConfirmCopyDolphinDataAsync() =>
        new YesNoWindow()
            .SetMainText(t("question.recomp_nand_mode.title"))
            .SetExtraText(t("question.recomp_nand_mode.extra"))
            .SetButtonText(t("action.recomp_nand_copy"), t("action.recomp_nand_share"))
            .AwaitAnswer();

    public Task<bool> ConfirmOfflineInstallAsync() =>
        new YesNoWindow()
            .SetButtonText(t("action.recomp_install_offline"), t("action.cancel"))
            .SetMainText(t("question.recomp_retro_wfc_unavailable.title"))
            .SetExtraText(t("question.recomp_retro_wfc_unavailable.extra"))
            .AwaitAnswer();

    public async Task<OperationResult> RunAsync(RecompOperationKind kind, Func<RecompOperation, Task<OperationResult>> operation)
    {
        using var cancellation = new CancellationTokenSource();
        var goal = t(
            kind switch
            {
                RecompOperationKind.Install => "progress.installing_recomp",
                RecompOperationKind.CopyNand => "progress.recomp_copying_nand",
                _ => "progress.updating_recomp",
            }
        );
        var window = new ProgressWindow(goal).SetGoal(goal).SetExtraText(t("progress.this_may_take_a_while"));
        if (kind != RecompOperationKind.CopyNand)
            window.SetCancellationTokenSource(cancellation);
        var finished = false;
        var progress = new Progress<RecompOperationProgress>(update =>
        {
            if (finished)
                return;
            if (update.Message is not null)
                window.SetExtraText(update.Message);
            if (update.Goal is not null)
                window.SetGoal(update.Goal);
            if (update.TotalBytes is not null)
                window.SetGoal(update.TotalBytes.Value / (1024d * 1024d));
            if (update.Percent is not null)
                window.UpdateProgress(update.Percent.Value);
            if (update.CanCancel is not null)
                window.SetCancellationTokenSource(update.CanCancel.Value ? cancellation : null);
        });
        window.Show();
        try
        {
            return await operation(new(progress, cancellation.Token));
        }
        finally
        {
            finished = true;
            window.SetCancellationTokenSource(null);
            window.Close();
        }
    }
}
