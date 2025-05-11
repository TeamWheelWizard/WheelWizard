using System.IO.Abstractions;

namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionSingletonService
{
    List<IDistribution> GetAllDistributions();
    RetroRewind RetroRewind { get; }
}

public class CustomDistributionSingletonService : ICustomDistributionSingletonService
{
    public IFileSystem FileSystem { get; }
    public RetroRewind RetroRewind { get; }
    public CustomDistributionSingletonService(IFileSystem fileSystem)
    {
        FileSystem = fileSystem;
        RetroRewind = new RetroRewind(fileSystem);
    }

    public List<IDistribution> GetAllDistributions()
    {
        return [RetroRewind];
    }
}
