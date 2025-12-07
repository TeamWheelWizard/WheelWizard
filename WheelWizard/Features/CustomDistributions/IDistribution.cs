using Semver;
using WheelWizard.Models.Enums;
using WheelWizard.Views.Popups.Generic;

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
    Task<OperationResult> InstallAsync(ProgressWindow progressWindow);

    /// <summary>
    /// Update the distribution.
    /// </summary>
    Task<OperationResult> UpdateAsync(ProgressWindow progressWindow);

    Task<OperationResult> RemoveAsync(ProgressWindow progressWindow);

    Task<OperationResult> ReinstallAsync(ProgressWindow progressWindow);

    Task<OperationResult<WheelWizardStatus>> GetCurrentStatusAsync();

    SemVersion? GetCurrentVersion();
}
