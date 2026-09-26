using WheelWizard.CustomDistributions;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Distributions;

// Existing screens retain ownership of their windows while consuming the feature's progress contract.
public static class DistributionPresentation
{
    public static Task<OperationResult> InstallAsync(this IDistribution distribution, ProgressWindow window) =>
        RunAsync(window, distribution.InstallAsync);

    public static Task<OperationResult> UpdateAsync(this IDistribution distribution, ProgressWindow window) =>
        RunAsync(window, distribution.UpdateAsync);

    public static Task<OperationResult> RemoveAsync(this IDistribution distribution, ProgressWindow window) =>
        RunAsync(window, distribution.RemoveAsync);

    public static Task<OperationResult> ReinstallAsync(this IDistribution distribution, ProgressWindow window) =>
        RunAsync(window, distribution.ReinstallAsync);

    private static async Task<OperationResult> RunAsync(ProgressWindow window, Func<DistributionOperation, Task<OperationResult>> action)
    {
        using var cancellation = new CancellationTokenSource();
        var active = true;
        var progress = new Progress<DistributionProgress>(value =>
        {
            if (!active)
                return;
            if (value.Message != null)
                window.SetExtraText(value.Message);
            if (value.Goal != null)
                window.SetGoal(value.Goal);
            if (value.TotalBytes != null)
                window.SetGoal(value.TotalBytes.Value / (1024d * 1024d));
            if (value.Percent != null)
                window.UpdateProgress(value.Percent.Value);
            if (value.CanCancel != null)
                window.SetCancellationTokenSource(value.CanCancel.Value ? cancellation : null);
        });
        try
        {
            return await action(new(progress, cancellation.Token));
        }
        finally
        {
            active = false;
            if (cancellation.IsCancellationRequested)
                window.MarkCancellationRequested();
            window.SetCancellationTokenSource(null);
        }
    }
}
