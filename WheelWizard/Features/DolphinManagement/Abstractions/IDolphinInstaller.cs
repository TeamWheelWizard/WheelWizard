namespace WheelWizard.DolphinManagement.Abstractions;

public interface IDolphinInstaller
{
    //IReadOnlyList<DolphinInstallation> AvailableInstallationMethods();
    Task<OperationResult> InstallDolphin(IProgress<int>? progress);
}
