using WheelWizard.CustomDistributions;
using WheelWizard.Models.Enums;

namespace WheelWizard.Launching;

public sealed class RrBetaLauncher(
    IRetroRewindLaunchService launch,
    ICustomDistributionSingletonService distributions,
    IDistributionOperationPresentation presentation
) : ILauncher
{
    public string GameTitle => "Retro Rewind Beta";

    public Task<OperationResult> Launch() => launch.LaunchAsync(true);

    public Task<OperationResult> Install() => presentation.RunAsync(distributions.RetroRewindBeta, DistributionAction.Install);

    public Task<OperationResult> Update() => presentation.RunAsync(distributions.RetroRewindBeta, DistributionAction.Update);

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var result = await distributions.RetroRewindBeta.GetCurrentStatusAsync();
        return result.IsSuccess ? result.Value : WheelWizardStatus.NotInstalled;
    }
}
