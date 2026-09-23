using System.Text.Json;
using WheelWizard.CloudSync.Backup;
using WheelWizard.CloudSync.Conflict;
using WheelWizard.CloudSync.Enrollment;
using WheelWizard.CloudSync.Profile;
using WheelWizard.CloudSync.Providers;
using WheelWizard.CustomDistributions;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>
/// Cloud transport for one license held in the local vault. The package is a
/// complete, valid rksys container because the existing package format requires
/// one, but all other license slots are blanked before it leaves the device.
/// </summary>
public sealed class VirtualProfileCloudService(
    IVirtualProfileVaultService vault,
    ICloudProfileService packages,
    ICloudProviderResolver providers,
    ICloudConflictResolver conflicts,
    IProfileBackupService backups,
    IRetroWfcEnrollmentService enrollment,
    ICustomDistributionSingletonService distributions,
    ISettingsManager settings
) : IVirtualProfileCloudService
{
    private const int RksysHeaderSize = 0x08;
    private const int RkpdSize = 0x8CC0;
    private const int SlotCount = 4;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<CloudSyncResult> PullAsync(Guid profileId) => SynchronizeAsync(profileId, pull: true);

    public Task<CloudSyncResult> PushAsync(Guid profileId) => SynchronizeAsync(profileId, pull: false);

    public async Task<CloudSyncResult> PullLocalSlotAsync(Guid profileId, int localSlot)
    {
        try
        {
            await CaptureLocalSlotAsync(profileId, localSlot);
            // Do not record the downloaded revision until it was successfully written into the
            // physical save slot. Otherwise a failed apply would make the next sync believe an
            // older local license is a new change and allow it to overwrite the cloud copy.
            return await SynchronizeAsync(
                profileId,
                pull: true,
                beforePullStateCommit: () => ApplyVaultProfileToLocalSlotAsync(profileId, localSlot)
            );
        }
        catch (Exception ex)
        {
            return CloudSyncResult.Fail($"Local profile could not be prepared for cloud sync: {ex.Message}");
        }
    }

    public async Task<CloudSyncResult> PushLocalSlotAsync(Guid profileId, int localSlot)
    {
        try
        {
            await CaptureLocalSlotAsync(profileId, localSlot);
            return await PushAsync(profileId);
        }
        catch (Exception ex)
        {
            return CloudSyncResult.Fail($"Local profile could not be prepared for cloud sync: {ex.Message}");
        }
    }

    public async Task<CloudSyncResult> DownloadToVaultAsync(Guid profileId)
    {
        await _gate.WaitAsync();
        try
        {
            var provider = providers.Resolve(GetProviderType());
            var remote = await ReadManifestAsync(provider, profileId);
            if (remote is null)
                return CloudSyncResult.Fail("The selected cloud profile no longer exists.");

            return await DownloadAndStoreAsync(provider, remote, profileId, updateState: true);
        }
        catch (Exception ex)
        {
            return CloudSyncResult.Fail($"Cloud profile could not be downloaded without changing the local save: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CloudSyncResult> SynchronizeAsync(Guid profileId, bool pull, Func<Task>? beforePullStateCommit = null)
    {
        await _gate.WaitAsync();
        try
        {
            var localRecord = await vault.GetAsync(profileId);
            if (pull && localRecord is null)
                return await DownloadToVaultCoreAsync(profileId);
            if (localRecord is null)
                return CloudSyncResult.Fail("The selected local profile is no longer available in the profile vault.");

            var provider = providers.Resolve(GetProviderType());
            var local = await CreatePackageAsync(localRecord);
            var deviceId = DeviceId;
            var state = await ReadStateAsync(profileId, deviceId);
            var remote = await ReadManifestAsync(provider, profileId);
            if (remote is null)
                return pull
                    ? CloudSyncResult.Ok(CloudSyncAction.NoOp, "No cloud copy exists for this profile yet.")
                    : await UploadAsync(provider, local, profileId, state, null);

            var comparison = await conflicts.CompareAsync(
                new CloudSyncSnapshot(local.Manifest, state.LastKnownCloudRevision, state.LastKnownCloudHash),
                new CloudSyncSnapshot(remote, state.LastKnownCloudRevision, state.LastKnownCloudHash)
            );
            if (comparison.Kind == ConflictKind.Conflict)
                return new CloudSyncResult(false, CloudSyncAction.Conflict, comparison.Message, comparison.Kind);
            if (comparison.Kind == ConflictKind.NoOp)
            {
                await WriteStateAsync(
                    profileId,
                    deviceId,
                    state with
                    {
                        LastKnownCloudRevision = remote.Revision,
                        LastKnownCloudHash = remote.ContentHash,
                        LastLocalHash = local.Manifest.ContentHash,
                    }
                );
                return CloudSyncResult.Ok(CloudSyncAction.NoOp, comparison.Message);
            }

            if (pull)
            {
                if (comparison.Kind == ConflictKind.SafePush)
                    return CloudSyncResult.Ok(CloudSyncAction.NoOp, "Local changes will be uploaded after WiiCompiled exits.");
                return await DownloadAndStoreAsync(
                    provider,
                    remote,
                    profileId,
                    updateState: true,
                    beforeStateCommit: beforePullStateCommit
                );
            }

            if (comparison.Kind == ConflictKind.SafePull)
                return new CloudSyncResult(
                    false,
                    CloudSyncAction.Conflict,
                    "Cloud changed while this profile was unchanged locally. Pull it before playing.",
                    comparison.Kind
                );
            return await UploadAsync(provider, local, profileId, state, remote);
        }
        catch (Exception ex)
        {
            return CloudSyncResult.Fail($"Cloud sync failed without replacing a local license: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    // Called only while the semaphore is held by SynchronizeAsync.
    private async Task<CloudSyncResult> DownloadToVaultCoreAsync(Guid profileId)
    {
        var provider = providers.Resolve(GetProviderType());
        var remote = await ReadManifestAsync(provider, profileId);
        return remote is null
            ? CloudSyncResult.Fail("The selected cloud profile no longer exists.")
            : await DownloadAndStoreAsync(provider, remote, profileId, updateState: true);
    }

    private async Task<CloudSyncResult> DownloadAndStoreAsync(
        ICloudProvider provider,
        CloudProfileManifest remote,
        Guid profileId,
        bool updateState,
        Func<Task>? beforeStateCommit = null
    )
    {
        if (remote.ProfileId != profileId)
            return CloudSyncResult.Fail("Cloud manifest profile ID does not match the selected profile.");

        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-vault-{profileId:N}.zip");
        try
        {
            await provider.DownloadAsync(RemotePath(profileId, "profile.zip"), temporary);
            var downloaded = await packages.ReadPackageAsync(temporary);
            await packages.ValidateProfileAsync(downloaded);
            if (
                downloaded.Manifest.ProfileId != remote.ProfileId
                || downloaded.Manifest.ContentHash != remote.ContentHash
                || downloaded.Manifest.Revision != remote.Revision
            )
                return CloudSyncResult.Fail("Cloud package does not match its manifest; no local license was replaced.");

            var license = ExtractOnlyLicense(downloaded.RksysData);
            // The backup is taken before the Mii database is changed as well as before the vault item is written.
            await backups.CreateBackupAsync();
            var existing = await vault.GetAsync(profileId);
            await vault.ImportAsync(profileId, license, downloaded.MiiData, existing?.IsLocalMirror == true);
            EnsureVaultProfileIsSelectedForSync(profileId);
            if (updateState)
            {
                if (beforeStateCommit is not null)
                    await beforeStateCommit();
                var deviceId = DeviceId;
                var state = await ReadStateAsync(profileId, deviceId);
                await WriteStateAsync(
                    profileId,
                    deviceId,
                    state with
                    {
                        LastKnownCloudRevision = remote.Revision,
                        LastKnownCloudHash = remote.ContentHash,
                        LastLocalHash = remote.ContentHash,
                    }
                );
            }
            return CloudSyncResult.Ok(
                CloudSyncAction.Pulled,
                "Cloud license was validated, backed up locally, and added to the profile vault."
            );
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private async Task<CloudSyncResult> UploadAsync(
        ICloudProvider provider,
        CloudProfilePackage local,
        Guid profileId,
        CloudSyncLocalState state,
        CloudProfileManifest? remote
    )
    {
        var revision = (remote?.Revision ?? 0) + 1;
        var deviceId = DeviceId;
        var devices = (remote?.Devices ?? [])
            .Where(device => device.DeviceId != deviceId)
            .Append(
                new CloudDeviceRecord(
                    deviceId,
                    Environment.MachineName,
                    Environment.OSVersion.Platform.ToString(),
                    DateTime.UtcNow,
                    DateTime.UtcNow,
                    await enrollment.GetStateAsync()
                )
            )
            .ToList();
        local.Manifest = CloudProfileService.CreateManifest(local, profileId, deviceId, revision, devices);

        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-vault-{profileId:N}-upload.zip");
        try
        {
            await packages.WritePackageAsync(local, temporary);
            await provider.UploadAsync(temporary, RemotePath(profileId, "profile.zip"));
            var manifestPath = temporary + ".json";
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(local.Manifest));
            await provider.UploadAsync(manifestPath, RemotePath(profileId, "manifest.json"));
            await WriteStateAsync(
                profileId,
                deviceId,
                state with
                {
                    LastKnownCloudRevision = revision,
                    LastKnownCloudHash = local.Manifest.ContentHash,
                    LastLocalHash = local.Manifest.ContentHash,
                    EnrollmentState = await enrollment.GetStateAsync(),
                }
            );
            return CloudSyncResult.Ok(CloudSyncAction.Pushed, "Profile vault license was uploaded after WiiCompiled exited.");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            if (File.Exists(temporary + ".json"))
                File.Delete(temporary + ".json");
        }
    }

    private async Task<CloudProfilePackage> CreatePackageAsync(VirtualProfileRecord record)
    {
        var sourcePath = distributions.RetroRewind.FindExistingRksysPath() ?? PathManager.GetRetroWfcSavePath();
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("RetroWFC rksys.dat was not found for the profile vault.");
        var rksys = await File.ReadAllBytesAsync(sourcePath);
        if (rksys.Length < 0x2BC000)
            throw new InvalidDataException("rksys.dat is too small to create a profile-vault package.");
        for (var slot = 0; slot < SlotCount; slot++)
            Array.Clear(rksys, RksysHeaderSize + slot * RkpdSize, RkpdSize);
        Buffer.BlockCopy(record.LicenseData, 0, rksys, RksysHeaderSize, RkpdSize);
        GameLicenseSingletonService.FixRksysCrc(rksys);
        var preview = new CloudLicensePreview(0, record.Name, record.FriendCode, record.Vr, record.Br, true);
        var package = new CloudProfilePackage
        {
            ProfileId = record.ProfileId,
            ProfileName = record.Name,
            RksysData = rksys,
            MiiData = record.MiiData,
            MiiMissingOnSource = record.MiiData is null,
            LicensePreviews = [preview],
            Manifest = new CloudProfileManifest { ProfileId = record.ProfileId, LastDeviceId = DeviceId },
        };
        package.Manifest = CloudProfileService.CreateManifest(package, record.ProfileId, DeviceId, 0, []);
        return package;
    }

    private async Task CaptureLocalSlotAsync(Guid profileId, int localSlot)
    {
        if (localSlot is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(localSlot));
        var sourcePath = distributions.RetroRewind.FindExistingRksysPath() ?? PathManager.GetRetroWfcSavePath();
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("RetroWFC rksys.dat was not found for the selected local profile.");
        var rksys = await File.ReadAllBytesAsync(sourcePath);
        var offset = RksysHeaderSize + localSlot * RkpdSize;
        if (rksys.Length < offset + RkpdSize || !rksys.AsSpan(offset, 4).SequenceEqual("RKPD"u8))
            throw new InvalidDataException("The selected local Mario Kart license no longer exists.");
        await vault.ImportAsync(profileId, rksys.AsSpan(offset, RkpdSize).ToArray(), null, isLocalMirror: true);
    }

    private async Task ApplyVaultProfileToLocalSlotAsync(Guid profileId, int localSlot)
    {
        if (localSlot is < 0 or >= SlotCount)
            throw new ArgumentOutOfRangeException(nameof(localSlot));
        var profile =
            await vault.GetAsync(profileId)
            ?? throw new InvalidDataException("The downloaded cloud profile is missing from the local vault.");
        var targetPath = distributions.RetroRewind.FindExistingRksysPath() ?? PathManager.GetRetroWfcSavePath();
        if (!File.Exists(targetPath))
            throw new FileNotFoundException("RetroWFC rksys.dat was not found for the selected local profile.");
        var rksys = await File.ReadAllBytesAsync(targetPath);
        var offset = RksysHeaderSize + localSlot * RkpdSize;
        if (rksys.Length < 0x2BC000 || rksys.Length < offset + RkpdSize)
            throw new InvalidDataException("rksys.dat is too small to apply the cloud license.");

        await backups.CreateBackupAsync();
        await vault.EnsureMiiPresentAsync(profile);
        Buffer.BlockCopy(profile.LicenseData, 0, rksys, offset, RkpdSize);
        GameLicenseSingletonService.FixRksysCrc(rksys);
        var temporary = targetPath + ".cloud-profile.tmp";
        await File.WriteAllBytesAsync(temporary, rksys);
        File.Move(temporary, targetPath, overwrite: true);
    }

    private static byte[] ExtractOnlyLicense(byte[] rksys)
    {
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var offset = RksysHeaderSize + slot * RkpdSize;
            if (rksys.Length >= offset + RkpdSize && rksys.AsSpan(offset, 4).SequenceEqual("RKPD"u8))
                return rksys.AsSpan(offset, RkpdSize).ToArray();
        }
        throw new InvalidDataException("The cloud package has no Mario Kart license.");
    }

    private async Task<CloudProfileManifest?> ReadManifestAsync(ICloudProvider provider, Guid profileId)
    {
        var remotePath = RemotePath(profileId, "manifest.json");
        if (!await provider.ExistsAsync(remotePath))
            return null;
        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-vault-{profileId:N}-manifest.json");
        try
        {
            await provider.DownloadAsync(remotePath, temporary);
            return JsonSerializer.Deserialize<CloudProfileManifest>(await File.ReadAllTextAsync(temporary))
                ?? throw new InvalidDataException("Cloud manifest is empty.");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private CloudProviderType GetProviderType() =>
        Enum.TryParse<CloudProviderType>(settings.Get<string>(settings.CLOUD_PROVIDER_TYPE), true, out var type)
            ? type
            : CloudProviderType.WebDav;

    private Guid DeviceId => Guid.TryParse(settings.Get<string>(settings.CLOUD_DEVICE_ID), out var id) ? id : Guid.Empty;

    private static string RemotePath(Guid profileId, string name) => $"/WheelWizard/CloudSaves/{profileId:D}/{name}";

    private static string StatePath(Guid profileId, Guid deviceId) =>
        Path.Combine(PathManager.CloudSyncStateFolderPath, $"{profileId:D}-{deviceId:D}.json");

    private static async Task<CloudSyncLocalState> ReadStateAsync(Guid profileId, Guid deviceId)
    {
        var path = StatePath(profileId, deviceId);
        return !File.Exists(path)
            ? new CloudSyncLocalState()
            : JsonSerializer.Deserialize<CloudSyncLocalState>(await File.ReadAllTextAsync(path)) ?? new CloudSyncLocalState();
    }

    private static async Task WriteStateAsync(Guid profileId, Guid deviceId, CloudSyncLocalState state)
    {
        var path = StatePath(profileId, deviceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state));
    }

    private void EnsureVaultProfileIsSelectedForSync(Guid profileId)
    {
        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(settings.Get<string>(settings.CLOUD_SYNC_PROFILE_IDS)) ?? [];
            var key = $"vault:{profileId:D}";
            if (keys.Contains(key, StringComparer.Ordinal))
                return;
            keys.Add(key);
            settings.Set(settings.CLOUD_SYNC_PROFILE_IDS, JsonSerializer.Serialize(keys));
        }
        catch (JsonException)
        {
            settings.Set(settings.CLOUD_SYNC_PROFILE_IDS, JsonSerializer.Serialize(new[] { $"vault:{profileId:D}" }));
        }
    }
}
