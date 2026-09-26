using System.Text.Json;
using WheelWizard.CloudSync.Conflict;
using WheelWizard.CloudSync.Enrollment;
using WheelWizard.CloudSync.Profile;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.CloudSync.Providers;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.CloudSync;

public sealed class CloudSyncService(
    ICloudProfileService profiles,
    ICloudProviderResolver providers,
    ICloudConflictResolver conflicts,
    IRetroWfcEnrollmentService enrollment,
    ISettingsManager settings,
    IVirtualProfileCloudService vaultProfiles,
    IProfileCloudBindingService profileBindings,
    IGameLicenseSingletonService gameLicenses
) : ICloudSyncService
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    public async Task<CloudSyncResult> SyncNowAsync()
    {
        var pull = await SynchronizeAsync(preLaunch: true, respectPhaseSetting: false);
        if (!pull.Success || pull.Action is CloudSyncAction.Pulled or CloudSyncAction.Conflict or CloudSyncAction.Disabled)
            return pull;

        // The settings page is only usable after the game window has closed. A manual sync first
        // protects against a newer cloud copy, then performs the same post-exit push pipeline.
        return await SynchronizeAsync(preLaunch: false, respectPhaseSetting: false);
    }

    public Task<CloudSyncResult> PreLaunchSyncAsync() => SynchronizeAsync(preLaunch: true, respectPhaseSetting: true);

    public Task<CloudSyncResult> PostLaunchSyncAsync() => SynchronizeAsync(preLaunch: false, respectPhaseSetting: true);

    public Task<CloudSyncResult> DownloadProfileToVaultAsync(Guid profileId) => vaultProfiles.DownloadToVaultAsync(profileId);

    public async Task<CloudSyncStatus> GetStatusAsync()
    {
        var profileId = GetOrCreateProfileId();
        var deviceId = GetDeviceId();
        var enrollmentState = await enrollment.GetStateAsync();
        return new CloudSyncStatus(
            settings.Get<bool>(settings.CLOUD_SYNC_ENABLED),
            settings.Get<string>(settings.CLOUD_PROVIDER_TYPE),
            profileId == Guid.Empty ? null : profileId,
            deviceId,
            enrollmentState,
            "Cloud saves support both online and offline licenses. NAND and console identity remain local."
        );
    }

    public async Task<IReadOnlyList<CloudProfileManifest>> GetAvailableProfilesAsync()
    {
        var provider = providers.Resolve(GetProviderType());
        var profileIds = (await provider.ListAsync("/WheelWizard/CloudSaves")).SelectMany(ExtractProfileIds).Distinct().ToList();
        var manifests = new List<CloudProfileManifest>();
        foreach (var profileId in profileIds)
        {
            var manifest = await ReadRemoteManifestAsync(provider, profileId);
            if (manifest is not null && manifest.ProfileId == profileId)
                manifests.Add(manifest);
        }
        return manifests.OrderByDescending(manifest => manifest.LastModifiedUtc).ToList();
    }

    private async Task<CloudSyncResult> SynchronizeAsync(bool preLaunch, bool respectPhaseSetting)
    {
        if (!settings.Get<bool>(settings.CLOUD_SYNC_ENABLED))
            return CloudSyncResult.Ok(CloudSyncAction.Disabled, "Cloud saves are disabled.");
        if (respectPhaseSetting && !settings.Get<bool>(preLaunch ? settings.SYNC_BEFORE_LAUNCH : settings.SYNC_AFTER_LAUNCH))
            return CloudSyncResult.Ok(CloudSyncAction.Disabled, "Cloud sync is disabled for this launch phase.");
        var profileId = GetOrCreateProfileId();

        await _syncGate.WaitAsync();
        try
        {
            var selection = ReadSelectedProfiles();
            if (selection.HasExplicitSelection)
            {
                CloudSyncResult? selectedResult = null;
                foreach (var localSlot in selection.LocalSlots)
                {
                    if (localSlot < 0 || localSlot >= gameLicenses.LicenseCollection.Users.Count)
                        return CloudSyncResult.Fail("The selected local Mario Kart license no longer exists.");
                    var selectedProfileId = await profileBindings.GetProfileIdAsync(
                        localSlot,
                        ProfileCloudBindingIdentity.Create(gameLicenses.LicenseCollection.Users[localSlot])
                    );
                    var result = preLaunch
                        ? await vaultProfiles.PullLocalSlotAsync(selectedProfileId, localSlot)
                        : await vaultProfiles.PushLocalSlotAsync(selectedProfileId, localSlot);
                    if (!result.Success || result.Action == CloudSyncAction.Conflict)
                        return result;
                    if (result.Action is CloudSyncAction.Pulled or CloudSyncAction.Pushed)
                        selectedResult = result;
                }
                foreach (var vaultProfileId in selection.VaultProfiles)
                {
                    var result = preLaunch ? await vaultProfiles.PullAsync(vaultProfileId) : await vaultProfiles.PushAsync(vaultProfileId);
                    if (!result.Success || result.Action == CloudSyncAction.Conflict)
                        return result;
                    if (result.Action is CloudSyncAction.Pulled or CloudSyncAction.Pushed)
                        selectedResult = result;
                }
                return selectedResult ?? CloudSyncResult.Ok(CloudSyncAction.NoOp, "Selected cloud profiles are already up to date.");
            }

            var provider = providers.Resolve(GetProviderType());
            var primary = preLaunch ? await PullBeforeLaunchAsync(provider, profileId) : await PushAfterLaunchAsync(provider, profileId);
            if (!primary.Success || primary.Action == CloudSyncAction.Conflict)
                return primary;

            return primary;
        }
        catch (Exception ex)
        {
            return CloudSyncResult.Fail($"Cloud sync failed without changing the local profile: {ex.Message}");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<CloudSyncResult> PullBeforeLaunchAsync(ICloudProvider provider, Guid profileId)
    {
        var deviceId = GetDeviceId();
        var state = await ReadStateAsync(profileId, deviceId);
        var local = await profiles.CaptureProfileAsync();
        var remote = await ReadRemoteManifestAsync(provider, profileId);
        if (remote is null)
            return CloudSyncResult.Ok(CloudSyncAction.NoOp, "No cloud profile exists yet; local profile was left unchanged.");
        if (remote.ProfileId != profileId)
            return CloudSyncResult.Fail("Cloud manifest profile ID does not match the selected profile.");

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
        if (comparison.Kind == ConflictKind.SafePush)
            return CloudSyncResult.Ok(CloudSyncAction.NoOp, "Local profile is newer; upload is deferred until WiiCompiled exits.");

        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-cloud-{profileId:N}.zip");
        try
        {
            await provider.DownloadAsync(RemotePath(profileId, "profile.zip"), temporary);
            var downloaded = await profiles.ReadPackageAsync(temporary);
            await profiles.ValidateProfileAsync(downloaded);
            if (
                downloaded.Manifest.ProfileId != remote.ProfileId
                || downloaded.Manifest.ContentHash != remote.ContentHash
                || downloaded.Manifest.Revision != remote.Revision
            )
                return CloudSyncResult.Fail("Cloud package does not match its manifest; local profile was not applied.");

            await profiles.ApplyProfileAsync(downloaded); // Profile service creates the mandatory local backup immediately before writing.
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
            return CloudSyncResult.Ok(CloudSyncAction.Pulled, "Cloud profile was validated, backed up locally, and applied.");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private async Task<CloudSyncResult> PushAfterLaunchAsync(ICloudProvider provider, Guid profileId)
    {
        var deviceId = GetDeviceId();
        var state = await ReadStateAsync(profileId, deviceId);
        var local = await profiles.CaptureProfileAsync();
        var remote = await ReadRemoteManifestAsync(provider, profileId);
        if (remote is not null)
        {
            var comparison = await conflicts.CompareAsync(
                new CloudSyncSnapshot(local.Manifest, state.LastKnownCloudRevision, state.LastKnownCloudHash),
                new CloudSyncSnapshot(remote, state.LastKnownCloudRevision, state.LastKnownCloudHash)
            );
            if (comparison.Kind == ConflictKind.Conflict)
                return new CloudSyncResult(false, CloudSyncAction.Conflict, comparison.Message, comparison.Kind);
            if (comparison.Kind == ConflictKind.NoOp)
                return CloudSyncResult.Ok(CloudSyncAction.NoOp, comparison.Message);
            if (comparison.Kind == ConflictKind.SafePull)
                return new CloudSyncResult(
                    false,
                    CloudSyncAction.Conflict,
                    "Cloud changed while this device did not; pull it on the next launch instead.",
                    ConflictKind.SafePull
                );
        }

        var revision = (remote?.Revision ?? 0) + 1;
        var devices = remote?.Devices ?? [];
        devices = devices
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
        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-cloud-{profileId:N}-upload.zip");
        try
        {
            await profiles.WritePackageAsync(local, temporary);
            // Publish package first and manifest last: readers never apply a revision whose data has not uploaded.
            await provider.UploadAsync(temporary, RemotePath(profileId, "profile.zip"));
            await File.WriteAllTextAsync(temporary + ".json", JsonSerializer.Serialize(local.Manifest));
            await provider.UploadAsync(temporary + ".json", RemotePath(profileId, "manifest.json"));
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
            return CloudSyncResult.Ok(CloudSyncAction.Pushed, "Local profile was uploaded after WiiCompiled exited.");
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
            if (File.Exists(temporary + ".json"))
                File.Delete(temporary + ".json");
        }
    }

    private async Task<CloudProfileManifest?> ReadRemoteManifestAsync(ICloudProvider provider, Guid profileId)
    {
        var remotePath = RemotePath(profileId, "manifest.json");
        if (!await provider.ExistsAsync(remotePath))
            return null;
        var temporary = Path.Combine(Path.GetTempPath(), $"wheelwizard-cloud-{profileId:N}-manifest.json");
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

    private static string RemotePath(Guid profileId, string name) => $"/WheelWizard/CloudSaves/{profileId:D}/{name}";

    private static IEnumerable<Guid> ExtractProfileIds(string path)
    {
        var segments = (Uri.TryCreate(path, UriKind.Absolute, out var absolute) ? absolute.AbsolutePath : path).Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        return segments.Select(Uri.UnescapeDataString).Where(segment => Guid.TryParse(segment, out _)).Select(Guid.Parse);
    }

    private Guid GetOrCreateProfileId()
    {
        if (Guid.TryParse(settings.Get<string>(settings.CLOUD_PROFILE_ID), out var id) && id != Guid.Empty)
            return id;

        id = Guid.NewGuid();
        settings.Set(settings.CLOUD_PROFILE_ID, id.ToString("D"));
        return id;
    }

    private Guid GetDeviceId() => Guid.TryParse(settings.Get<string>(settings.CLOUD_DEVICE_ID), out var id) ? id : Guid.Empty;

    private CloudProviderType GetProviderType() =>
        Enum.TryParse<CloudProviderType>(settings.Get<string>(settings.CLOUD_PROVIDER_TYPE), true, out var type)
            ? type
            : CloudProviderType.WebDav;

    private ProfileSyncSelection ReadSelectedProfiles()
    {
        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(settings.Get<string>(settings.CLOUD_SYNC_PROFILE_IDS)) ?? [];
            var localSlots = keys.Where(key => key.StartsWith("local:", StringComparison.Ordinal))
                .Select(key => int.TryParse(key[6..], out var slot) ? slot : -1)
                .Where(slot => slot is >= 0 and < 4)
                .Distinct()
                .ToList();
            var vaultProfiles = keys.Where(key => key.StartsWith("vault:", StringComparison.Ordinal))
                .Select(key => Guid.TryParse(key[6..], out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();
            return new ProfileSyncSelection(keys.Count > 0, localSlots, vaultProfiles);
        }
        catch (JsonException)
        {
            return new ProfileSyncSelection(false, [], []);
        }
    }

    private sealed record ProfileSyncSelection(bool HasExplicitSelection, IReadOnlyList<int> LocalSlots, IReadOnlyList<Guid> VaultProfiles);

    private static string StatePath(Guid profile, Guid device) =>
        Path.Combine(PathManager.CloudSyncStateFolderPath, $"{profile:D}-{device:D}.json");

    private static async Task<CloudSyncLocalState> ReadStateAsync(Guid profile, Guid device)
    {
        var path = StatePath(profile, device);
        return !File.Exists(path)
            ? new CloudSyncLocalState()
            : JsonSerializer.Deserialize<CloudSyncLocalState>(await File.ReadAllTextAsync(path)) ?? new CloudSyncLocalState();
    }

    private static async Task WriteStateAsync(Guid profile, Guid device, CloudSyncLocalState state)
    {
        var path = StatePath(profile, device);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state));
    }
}
