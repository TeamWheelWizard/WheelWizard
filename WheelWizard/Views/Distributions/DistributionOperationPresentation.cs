using WheelWizard.CustomDistributions;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Distributions;

public sealed class DistributionOperationPresentation : IDistributionOperationPresentation
{
    public async Task<OperationResult> RunAsync(IDistribution distribution, DistributionAction action)
    {
        var title =
            distribution is RetroRewindBeta
                ? action == DistributionAction.Install
                    ? "Installing test build"
                    : "Updating test build"
                : "Progress Window";
        var window = new ProgressWindow(title);
        try
        {
            window.Show();
            return action switch
            {
                DistributionAction.Install => await distribution.InstallAsync(window),
                DistributionAction.Update => await distribution.UpdateAsync(window),
                DistributionAction.Remove => await distribution.RemoveAsync(window),
                DistributionAction.Reinstall => await distribution.ReinstallAsync(window),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
        }
        finally
        {
            window.Close();
        }
    }
}
