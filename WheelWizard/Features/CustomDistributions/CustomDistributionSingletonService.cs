namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionSingletonService
{
    List<IDistribution> GetDistributions();
}

public class CustomDistributionSingletonService : ICustomDistributionSingletonService
{
    readonly RetroRewind _retroRewind = new();
    
    public List<IDistribution> GetDistributions()
    {
        return [_retroRewind];
    }
}
