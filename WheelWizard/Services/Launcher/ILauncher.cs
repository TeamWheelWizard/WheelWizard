using WheelWizard.Models.Enums;

namespace WheelWizard.Services.Launcher;

public interface ILauncher
{
    public string GameTitle { get; }
    public Task Launch();
    public Task Install();
    public Task Update();
    public Task<WheelWizardStatus> GetCurrentStatus();
}
