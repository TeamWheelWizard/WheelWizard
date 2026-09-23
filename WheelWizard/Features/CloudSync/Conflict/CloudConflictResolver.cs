namespace WheelWizard.CloudSync.Conflict;

/// <summary>Classifies changes only. Resolution is deliberately a user decision, never an overwrite.</summary>
public sealed class CloudConflictResolver : ICloudConflictResolver
{
    public Task<ConflictResult> CompareAsync(CloudSyncSnapshot local, CloudSyncSnapshot remote)
    {
        if (string.Equals(local.Manifest.ContentHash, remote.Manifest.ContentHash, StringComparison.Ordinal))
            return Task.FromResult(new ConflictResult(ConflictKind.NoOp, "Local and cloud profiles are identical."));

        // A newly linked device has no common base revision. The selected cloud profile is its
        // explicit bootstrap source; ApplyProfileAsync creates a local backup before touching it.
        if (local.LastKnownCloudRevision == 0 && string.IsNullOrEmpty(local.LastKnownCloudHash))
            return Task.FromResult(
                new ConflictResult(ConflictKind.SafePull, "Applying the selected cloud profile to a newly linked device.")
            );

        var localChanged = !string.Equals(local.Manifest.ContentHash, local.LastKnownCloudHash, StringComparison.Ordinal);
        var remoteChanged =
            remote.Manifest.Revision != local.LastKnownCloudRevision
            || !string.Equals(remote.Manifest.ContentHash, local.LastKnownCloudHash, StringComparison.Ordinal);
        if (localChanged && remoteChanged)
            return Task.FromResult(new ConflictResult(ConflictKind.Conflict, "Both this device and the cloud profile changed."));
        return Task.FromResult(
            remoteChanged
                ? new ConflictResult(ConflictKind.SafePull, "Cloud profile is newer.")
                : new ConflictResult(ConflictKind.SafePush, "Local profile changed while cloud is unchanged.")
        );
    }

    public Task<ResolutionResult> ResolveAsync(ConflictResult conflict, ConflictResolutionStrategy strategy) =>
        Task.FromResult(
            strategy == ConflictResolutionStrategy.Cancel || conflict.Kind != ConflictKind.Conflict
                ? new ResolutionResult(false, "No destructive conflict resolution was performed.")
                : new ResolutionResult(false, "Conflict resolution must be applied explicitly by the settings UI.")
        );
}
