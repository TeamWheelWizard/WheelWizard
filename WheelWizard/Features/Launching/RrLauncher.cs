using WheelWizard.CustomDistributions;
using WheelWizard.Models.Enums;

namespace WheelWizard.Launching;

public sealed class RrLauncher(
    IRetroRewindLaunchService launch,
    ICustomDistributionSingletonService distributions,
    IDistributionOperationPresentation presentation
) : ILauncher
{
    public string GameTitle => "Retro Rewind";

    public Task<OperationResult> Launch() => launch.LaunchAsync(false);

    public Task<OperationResult> Install() => presentation.RunAsync(distributions.RetroRewind, DistributionAction.Install);

    public Task<OperationResult> Update() => presentation.RunAsync(distributions.RetroRewind, DistributionAction.Update);

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var result = await distributions.RetroRewind.GetCurrentStatusAsync();
        return result.IsSuccess ? result.Value : WheelWizardStatus.NotInstalled;
    }
}
