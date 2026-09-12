namespace WheelWizard.CustomDistributions;

public enum DistributionAction
{
    Install,
    Update,
    Remove,
    Reinstall,
}

public interface IDistributionOperationPresentation
{
    Task<OperationResult> RunAsync(IDistribution distribution, DistributionAction action);
}
