namespace WheelWizard.Launching;

public enum DolphinVersionAction
{
    Cancel,
    Update,
    PlayAnyway,
}

public interface IDolphinLaunchPresentation
{
    Task ShowUnverifiedVersionAsync();
    Task<DolphinVersionAction> ChooseOutdatedVersionActionAsync(string? version);
    void OpenUpdateInstructions(bool bundled);
    Task RunUpdateAsync(Func<IProgress<int>, Task<OperationResult>> update);
    void ShowLaunchFailure(string reason);
}
