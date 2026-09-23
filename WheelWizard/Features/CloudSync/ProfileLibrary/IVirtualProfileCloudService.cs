namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>
/// Synchronizes an individual vault license. A vault profile deliberately has its
/// own cloud ID and state file, so it never shares revisions or conflicts with a
/// different Mario Kart license.
/// </summary>
public interface IVirtualProfileCloudService
{
    Task<CloudSyncResult> PullAsync(Guid profileId);
    Task<CloudSyncResult> PushAsync(Guid profileId);
    Task<CloudSyncResult> DownloadToVaultAsync(Guid profileId);
    Task<CloudSyncResult> PullLocalSlotAsync(Guid profileId, int localSlot);
    Task<CloudSyncResult> PushLocalSlotAsync(Guid profileId, int localSlot);
}
