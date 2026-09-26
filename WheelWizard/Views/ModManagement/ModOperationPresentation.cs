using WheelWizard.Mods;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.ModManagement;

public sealed class ModOperationPresentation : IModOperationPresentation
{
    public async Task<TResult> RunAsync<TResult>(
        Func<IProgress<ModOperationProgress>, CancellationToken, Task<TResult>> operation,
        bool canCancel = false
    )
    {
        using var cancellation = new CancellationTokenSource();
        ProgressWindow? window = null;
        var finished = false;
        string? currentTitle = null;
        var progress = new Progress<ModOperationProgress>(update =>
        {
            if (finished)
                return;

            var title = update.Stage switch
            {
                ModOperationStage.Preparing => t("progress.combining_files"),
                ModOperationStage.Extracting => t("progress.installing_mod"),
                ModOperationStage.Installing => t("progress.installing_mods"),
                _ => t("progress.converting_mod_to_patches"),
            };
            if (window is not null && currentTitle != title)
            {
                window.SetCancellationTokenSource(null);
                window.Close();
                window = null;
            }
            if (window is null)
            {
                currentTitle = title;
                window = new ProgressWindow(title);
                if (canCancel)
                    window.SetCancellationTokenSource(cancellation);
                window.Show();
            }

            window.SetGoal(
                update.Stage switch
                {
                    ModOperationStage.Preparing => t("progress.preparing_files_count", update.TotalFiles ?? 0)!,
                    ModOperationStage.Extracting => t("state.extracting"),
                    ModOperationStage.Installing => t("progress.installing_mods_count", update.TotalFiles ?? 0)!,
                    ModOperationStage.Applying => t("progress.applying_converted_mod"),
                    _ => t("progress.converting_files_count", update.TotalFiles ?? 0)!,
                }
            );
            window.UpdateProgress(update.Percent);
            if (update.FileName is not null)
                window.SetExtraText(
                    update.Stage == ModOperationStage.Converting
                        ? t("progress.converting_file", update.FileName)!
                        : $"{t("state.installing")} {update.FileName}"
                );
        });

        try
        {
            return await operation(progress, cancellation.Token);
        }
        finally
        {
            finished = true;
            window?.SetCancellationTokenSource(null);
            window?.Close();
        }
    }
}
