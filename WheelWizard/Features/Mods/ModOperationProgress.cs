namespace WheelWizard.Mods;

public enum ModOperationStage
{
    Preparing,
    Extracting,
    Installing,
    Converting,
    Applying,
}

public sealed record ModOperationProgress(ModOperationStage Stage, int Percent, string? FileName = null, int? TotalFiles = null);

/// <summary>Owns presentation lifetime around a mod operation; services only report progress.</summary>
public interface IModOperationPresentation
{
    Task<TResult> RunAsync<TResult>(
        Func<IProgress<ModOperationProgress>, CancellationToken, Task<TResult>> operation,
        bool canCancel = false
    );
}
