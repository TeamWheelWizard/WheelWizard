namespace WheelWizard.DolphinManagent.Abstractions;

public interface IDolphinInstaller
{
    IReadOnlyList<DolphinInstallation> AvailableInstallationMethods();
    //bool InstallDolphin(DolphinInstallation method);
}
