using Semver;
using WheelWizard.Models.Enums;

namespace WheelWizard.CustomDistributions;

//todo: we cannot make more distributions before we also write a mystuff service and a service to download using UI

public interface IDistribution
{
    /// <summary>
    /// The title of the given distribution.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// The name of the primary folder where the distribution is installed within the wheelwizard folder.
    /// </summary>
    string FolderName { get; }

    /// <summary>
    /// The name of the wiiDisc .xml file in XMLFolderName
    /// </summary>
    string XMLFileName { get; }

    /// <summary>
    /// The name of the folder containing the distributions wiiDisc .xml file
    /// </summary>
    string XMLFolderName { get; }

    /// <summary>
    /// Install the distribution.
    /// </summary>
    Task<OperationResult> InstallAsync(DistributionOperation operation);

    /// <summary>
    /// Update the distribution.
    /// </summary>
    Task<OperationResult> UpdateAsync(DistributionOperation operation);

    Task<OperationResult> RemoveAsync(DistributionOperation operation);

    Task<OperationResult> ReinstallAsync(DistributionOperation operation);

    Task<OperationResult<WheelWizardStatus>> GetCurrentStatusAsync();

    SemVersion? GetCurrentVersion();
}
