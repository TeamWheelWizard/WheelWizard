using WheelWizard.CustomDistributions;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Recomp;

public enum RecompOperationKind
{
    Install,
    Update,
    PrepareLaunch,
    CopyNand,
}

public sealed record RecompOperationProgress(
    string? Message = null,
    int? Percent = null,
    string? Goal = null,
    long? TotalBytes = null,
    bool? CanCancel = null
);

public sealed record RecompOperation(IProgress<RecompOperationProgress>? Progress = null, CancellationToken CancellationToken = default)
{
    public void Report(RecompOperationProgress progress) => Progress?.Report(progress);

    public IProgress<RecompInstallProgress> InstallProgress => new InstallReporter(this);
    public DistributionOperation DistributionOperation => new(new DistributionReporter(this), CancellationToken);

    private sealed class InstallReporter(RecompOperation operation) : IProgress<RecompInstallProgress>
    {
        public void Report(RecompInstallProgress value) => operation.Report(new(value.Message, value.Percent));
    }

    private sealed class DistributionReporter(RecompOperation operation) : IProgress<DistributionProgress>
    {
        public void Report(DistributionProgress value) =>
            operation.Report(new(value.Message, value.Percent, value.Goal, value.TotalBytes, value.CanCancel));
    }
}

public interface IRecompPresentation
{
    Task<bool> ConfirmUseDolphinDataAsync();
    Task<bool> ConfirmCopyDolphinDataAsync();
    Task<bool> ConfirmOfflineInstallAsync();
    Task<OperationResult> RunAsync(RecompOperationKind kind, Func<RecompOperation, Task<OperationResult>> operation);
}
