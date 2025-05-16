using Semver;
using WheelWizard.Models.Enums;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.CustomDistributions;

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
    /// Install the distribution.
    /// </summary>
    Task<OperationResult> Install(ProgressWindow progressWindow);

    /// <summary>
    /// Update the distribution.
    /// </summary>
    Task<OperationResult> Update(ProgressWindow progressWindow );

    Task<OperationResult> Remove(ProgressWindow progressWindow);

    Task<OperationResult> Reinstall(ProgressWindow progressWindow);

    Task<OperationResult<WheelWizardStatus>> GetCurrentStatus();

    SemVersion? GetCurrentVersion();
}
