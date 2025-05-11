namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionSingletonService
{
    List<IDistribution> GetAllDistributions();
    RetroRewind RetroRewind { get; }
}

public class CustomDistributionSingletonService : ICustomDistributionSingletonService
{
    public RetroRewind RetroRewind { get; } = new RetroRewind();
    
    public List<IDistribution> GetAllDistributions()
    {
        return [RetroRewind];
    }
}
