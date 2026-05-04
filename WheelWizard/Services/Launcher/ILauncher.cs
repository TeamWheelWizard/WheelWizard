using WheelWizard.Models.Enums;

namespace WheelWizard.Services.Launcher;

public interface ILauncher
{
    public string GameTitle { get; }
    public Task<OperationResult> Launch();
    public Task<OperationResult> Install();
    public Task<OperationResult> Update();
    public Task<WheelWizardStatus> GetCurrentStatus();
}
