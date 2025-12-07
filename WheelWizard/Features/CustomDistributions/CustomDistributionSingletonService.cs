using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Shared.Services;

namespace WheelWizard.CustomDistributions;

public interface ICustomDistributionSingletonService
{
    List<IDistribution> GetAllDistributions();

    // FIXME: Abstract this reference away. A generic Distributions service kinda loses its purpose when you still have to reference a distribution by name (like done here)
    //  Instead you would want something like DistService.GetCurrentDistro()
    //  The rest of the application should not have to know what distribution is currently active.
    RetroRewind RetroRewind { get; }
}

public class CustomDistributionSingletonService : ICustomDistributionSingletonService
{
    public RetroRewind RetroRewind { get; }

    public CustomDistributionSingletonService(IFileSystem fileSystem, IApiCaller<IRetroRewindApi> api, ILogger<IDistribution> logger)
    {
        RetroRewind = new RetroRewind(fileSystem, api, logger);
    }

    public List<IDistribution> GetAllDistributions()
    {
        return [RetroRewind];
    }
}
