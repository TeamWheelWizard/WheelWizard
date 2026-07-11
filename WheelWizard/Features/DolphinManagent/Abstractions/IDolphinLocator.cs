namespace WheelWizard.DolphinManagent.Abstractions;

public interface IDolphinLocator
{
    IReadOnlyList<DolphinInstallation> DetectInstallations();
}
