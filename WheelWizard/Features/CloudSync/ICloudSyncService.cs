namespace WheelWizard.CloudSync;

public interface ICloudSyncService
{
    Task<CloudSyncResult> PreLaunchSyncAsync();
    Task<CloudSyncResult> PostLaunchSyncAsync();
    Task<CloudSyncResult> SyncNowAsync();
    Task<CloudSyncStatus> GetStatusAsync();
    Task<IReadOnlyList<CloudProfileManifest>> GetAvailableProfilesAsync();

    /// <summary>Downloads one cloud-only profile into the local vault without replacing rksys.dat.</summary>
    Task<CloudSyncResult> DownloadProfileToVaultAsync(Guid profileId);
}
