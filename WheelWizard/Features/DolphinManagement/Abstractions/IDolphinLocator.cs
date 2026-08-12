namespace WheelWizard.DolphinManagement.Abstractions;

public interface IDolphinLocator
{
    IReadOnlyList<DolphinInstallation> DetectInstallations();
}
