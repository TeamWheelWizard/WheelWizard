namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>Creates a reversible, per-launch rksys.dat view containing only the chosen local slots.</summary>
public interface IVisibleProfileLaunchService
{
    Task PrepareAsync();
    Task RestoreAsync();
}
