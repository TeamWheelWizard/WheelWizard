namespace WheelWizard.CloudSync.Conflict;

public sealed record CloudSyncSnapshot(CloudProfileManifest Manifest, long LastKnownCloudRevision, string LastKnownCloudHash);

public interface ICloudConflictResolver
{
    Task<ConflictResult> CompareAsync(CloudSyncSnapshot local, CloudSyncSnapshot remote);
    Task<ResolutionResult> ResolveAsync(ConflictResult conflict, ConflictResolutionStrategy strategy);
}
