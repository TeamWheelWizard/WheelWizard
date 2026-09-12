namespace WheelWizard.Shared.IO;

public enum DirectoryMoveOutcome
{
    NoOp,
    Success,
    CopyFailed,
    VerificationFailed,
    SourceDeletionFailed,
}

public sealed class DirectoryMoveContentsResult
{
    public DirectoryMoveContentsResult(
        DirectoryMoveOutcome outcome,
        string sourcePath,
        string destinationPath,
        bool copyAttempted,
        bool verificationAttempted,
        bool deleteSourceRequested,
        bool sourceDeletionSucceeded,
        string? errorMessage = null,
        Exception? exception = null,
        IReadOnlyList<string>? verificationFailures = null
    )
    {
        Outcome = outcome;
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        CopyAttempted = copyAttempted;
        VerificationAttempted = verificationAttempted;
        DeleteSourceRequested = deleteSourceRequested;
        SourceDeletionSucceeded = deleteSourceRequested ? sourceDeletionSucceeded : true;
        ErrorMessage = errorMessage;
        Exception = exception;
        VerificationFailures = verificationFailures ?? Array.Empty<string>();
    }

    public DirectoryMoveOutcome Outcome { get; }
    public string SourcePath { get; }
    public string DestinationPath { get; }
    public bool CopyAttempted { get; }
    public bool VerificationAttempted { get; }
    public bool DeleteSourceRequested { get; }
    public bool SourceDeletionSucceeded { get; }
    public string? ErrorMessage { get; }
    public Exception? Exception { get; }
    public IReadOnlyList<string> VerificationFailures { get; }

    public bool CopyCompleted => CopyAttempted && Outcome != DirectoryMoveOutcome.CopyFailed;
    public bool VerificationSucceeded => !VerificationAttempted || Outcome != DirectoryMoveOutcome.VerificationFailed;
    public bool IsSuccessful => Outcome is DirectoryMoveOutcome.Success or DirectoryMoveOutcome.NoOp;
    public bool RequiresUserDecision => Outcome == DirectoryMoveOutcome.SourceDeletionFailed;
}
