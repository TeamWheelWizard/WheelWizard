using System.IO.Abstractions;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Shared.Services;

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

    public CustomDistributionSingletonService(IFileSystem fileSystem, IApiCaller<IRetroRewindApi> api)
    {
        FileSystem = fileSystem;
        RetroRewind = new RetroRewind(fileSystem, api);
    }

    public List<IDistribution> GetAllDistributions()
    {
        return [RetroRewind];
    }
}
