using System.Text.Json;
using WheelWizard.CloudSync.Backup;
using WheelWizard.CustomDistributions;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.CloudSync.ProfileLibrary;

public sealed class VisibleProfileLaunchService(
    ICustomDistributionSingletonService distributions,
    IProfileBackupService backups,
    ISettingsManager settings,
    IGameLicenseSingletonService gameLicenses,
    IVirtualProfileVaultService vault,
    ICloudSyncService cloudSync
) : IVisibleProfileLaunchService
{
    private const int RksysHeaderSize = 0x08;
    private const int RkpdSize = 0x8CC0;
    private const int SlotCount = 4;

    private string SessionFolder => Path.Combine(PathManager.CloudSyncStateFolderPath, "visible-profile-session");
    private string OriginalPath => Path.Combine(SessionFolder, "original-rksys.dat");
    private string StatePath => Path.Combine(SessionFolder, "state.json");

    public async Task PrepareAsync()
    {
        if (!settings.Get<bool>(settings.CLOUD_SYNC_ENABLED))
            return;

        await RestoreAsync(); // Recover safely after a prior forced game exit before creating a new view.
        var selected = ReadSelection();
        if (selected.Count == 0)
            return;
        // A cloud-only entry is materialized in the vault first. This preserves every
        // physical save slot and lets a fifth cloud profile be selected for this launch.
        var requestedCloudProfiles = selected
            .Select(ParseCloudProfileId)
            .Where(profileId => profileId is not null)
            .Select(profileId => profileId!.Value);
        foreach (var profileId in requestedCloudProfiles)
        {
            var downloaded = await cloudSync.DownloadProfileToVaultAsync(profileId);
            if (!downloaded.Success)
                throw new InvalidOperationException(downloaded.Message);
        }

        var sourceSlots = selected
            .Select(ParseLocalSlot)
            .Where(slot => slot is >= 0 and < SlotCount)
            .Distinct()
            .Select(slot => slot!.Value)
            .ToList();
        var vaultProfiles = new List<VirtualProfileRecord>();
        foreach (
            var profileId in selected
                .Select(key => ParseVaultProfileId(key) ?? ParseCloudProfileId(key))
                .Where(profileId => profileId is not null)
                .Select(profileId => profileId!.Value)
                .Distinct()
        )
        {
            var profile =
                await vault.GetAsync(profileId)
                ?? throw new InvalidDataException("A selected virtual profile is no longer available in the local profile vault.");
            await vault.EnsureMiiPresentAsync(profile);
            vaultProfiles.Add(profile);
        }
        if (sourceSlots.Count + vaultProfiles.Count == 0)
            return;
        if (sourceSlots.Count + vaultProfiles.Count > SlotCount)
            throw new InvalidOperationException("WiiCompiled can display at most four selected profiles at once.");

        var rksysPath = distributions.RetroRewind.FindExistingRksysPath() ?? PathManager.GetRetroWfcSavePath();
        if (!File.Exists(rksysPath))
            throw new FileNotFoundException("RetroWFC rksys.dat was not found for the visible-profile launch view.");
        var original = await File.ReadAllBytesAsync(rksysPath);
        if (original.Length < RksysHeaderSize + SlotCount * RkpdSize)
            throw new InvalidDataException("rksys.dat is too small to filter its license slots.");

        await backups.CreateBackupAsync();
        Directory.CreateDirectory(SessionFolder);
        await File.WriteAllBytesAsync(OriginalPath, original);
        var view = original.ToArray();
        for (var slot = 0; slot < SlotCount; slot++)
            Array.Clear(view, RksysHeaderSize + slot * RkpdSize, RkpdSize);
        var maps = new List<SlotMap>();
        var targetSlot = 0;
        foreach (var sourceSlot in sourceSlots)
        {
            Buffer.BlockCopy(original, RksysHeaderSize + sourceSlot * RkpdSize, view, RksysHeaderSize + targetSlot * RkpdSize, RkpdSize);
            maps.Add(new SlotMap(targetSlot++, sourceSlot, null));
        }
        foreach (var virtualProfile in vaultProfiles)
        {
            Buffer.BlockCopy(virtualProfile.LicenseData, 0, view, RksysHeaderSize + targetSlot * RkpdSize, RkpdSize);
            maps.Add(new SlotMap(targetSlot++, null, virtualProfile.ProfileId));
        }
        // Mario Kart validates a CRC32 at 0x27FFC. The temporary slot view is still a complete
        // rksys.dat and needs the same checksum repair as every normal save write.
        GameLicenseSingletonService.FixRksysCrc(view);

        await File.WriteAllTextAsync(StatePath, JsonSerializer.Serialize(new VisibleProfileSession(rksysPath, maps)));
        await WriteAtomicAsync(rksysPath, view);
    }

    public async Task RestoreAsync()
    {
        if (!File.Exists(StatePath) || !File.Exists(OriginalPath))
            return;
        var state = JsonSerializer.Deserialize<VisibleProfileSession>(await File.ReadAllTextAsync(StatePath));
        if (state is null || !File.Exists(state.RksysPath))
            return;

        var original = await File.ReadAllBytesAsync(OriginalPath);
        var session = await File.ReadAllBytesAsync(state.RksysPath);
        if (original.Length < RksysHeaderSize + SlotCount * RkpdSize || session.Length < RksysHeaderSize + SlotCount * RkpdSize)
            throw new InvalidDataException("The visible-profile launch session is corrupt; the original save was left in its backup.");

        var validMaps = state.Maps.Where(map => map.TargetSlot is >= 0 and < SlotCount).ToList();
        foreach (var map in validMaps.Where(map => map.SourceSlot is >= 0 and < SlotCount))
            Buffer.BlockCopy(
                session,
                RksysHeaderSize + map.TargetSlot * RkpdSize,
                original,
                RksysHeaderSize + map.SourceSlot!.Value * RkpdSize,
                RkpdSize
            );
        foreach (var map in validMaps.Where(map => map.VaultProfileId is not null))
        {
            var licenseData = session.AsSpan(RksysHeaderSize + map.TargetSlot * RkpdSize, RkpdSize).ToArray();
            if (IsLicenseData(licenseData))
                await vault.UpdateAsync(map.VaultProfileId!.Value, licenseData);
        }

        // WiiCompiled may create a license in one of the intentionally blank view slots. Preserve
        // that new license by assigning it to an actually unused slot in the complete local save.
        // Existing unselected licenses are never overwritten.
        var mappedTargets = validMaps.Select(map => map.TargetSlot).ToHashSet();
        var freeOriginalSlots = new Queue<int>(Enumerable.Range(0, SlotCount).Where(slot => !HasLicense(original, slot)));
        foreach (var targetSlot in Enumerable.Range(0, SlotCount).Where(slot => !mappedTargets.Contains(slot)))
        {
            if (!HasLicense(session, targetSlot))
                continue;
            var licenseData = session.AsSpan(RksysHeaderSize + targetSlot * RkpdSize, RkpdSize).ToArray();
            if (freeOriginalSlots.Count > 0)
            {
                var destinationSlot = freeOriginalSlots.Dequeue();
                Buffer.BlockCopy(licenseData, 0, original, RksysHeaderSize + destinationSlot * RkpdSize, RkpdSize);
                EnsureProfileIsVisible($"local:{destinationSlot}");
            }
            else
            {
                // All four physical slots are occupied. Preserve a newly created license in the
                // vault rather than overwriting an unselected local profile.
                var created = await vault.CreateAsync(licenseData);
                EnsureVaultProfileIsSelectedForSync(created.ProfileId);
                EnsureProfileIsVisible($"vault:{created.ProfileId:D}");
            }
        }
        GameLicenseSingletonService.FixRksysCrc(original);
        await WriteAtomicAsync(state.RksysPath, original);
        Directory.Delete(SessionFolder, recursive: true);
        // The singleton otherwise keeps the pre-launch rksys.dat cached, so a newly created
        // license would not appear in WheelWizard until a later unrelated reload.
        gameLicenses.LoadLicense();
        ProfileLibraryChangeNotifier.NotifyChanged();
    }

    private IReadOnlyList<string> ReadSelection()
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(settings.Get<string>(settings.CLOUD_VISIBLE_PROFILE_IDS)) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int? ParseLocalSlot(string key) =>
        key.StartsWith("local:", StringComparison.Ordinal) && int.TryParse(key[6..], out var slot) ? slot : null;

    private static Guid? ParseVaultProfileId(string key) =>
        key.StartsWith("vault:", StringComparison.Ordinal) && Guid.TryParse(key[6..], out var profileId) ? profileId : null;

    private static Guid? ParseCloudProfileId(string key) =>
        key.StartsWith("cloud:", StringComparison.Ordinal) && Guid.TryParse(key[6..], out var profileId) ? profileId : null;

    private static bool HasLicense(byte[] rksys, int slot)
    {
        var offset = RksysHeaderSize + slot * RkpdSize;
        return rksys.Length >= offset + 4 && rksys.AsSpan(offset, 4).SequenceEqual("RKPD"u8);
    }

    private static bool IsLicenseData(byte[] licenseData) =>
        licenseData.Length == RkpdSize && licenseData.AsSpan(0, 4).SequenceEqual("RKPD"u8);

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

    private void EnsureProfileIsVisible(string profileKey)
    {
        try
        {
            var keys = JsonSerializer.Deserialize<List<string>>(settings.Get<string>(settings.CLOUD_VISIBLE_PROFILE_IDS)) ?? [];
            if (keys.Contains(profileKey, StringComparer.Ordinal) || keys.Count >= SlotCount)
                return;
            keys.Add(profileKey);
            settings.Set(settings.CLOUD_VISIBLE_PROFILE_IDS, JsonSerializer.Serialize(keys));
        }
        catch (JsonException)
        {
            settings.Set(settings.CLOUD_VISIBLE_PROFILE_IDS, JsonSerializer.Serialize(new[] { profileKey }));
        }
    }

    private static async Task WriteAtomicAsync(string path, byte[] data)
    {
        var temporary = path + ".visible-profiles.tmp";
        await File.WriteAllBytesAsync(temporary, data);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record VisibleProfileSession(string RksysPath, List<SlotMap> Maps);

    private sealed record SlotMap(int TargetSlot, int? SourceSlot, Guid? VaultProfileId);
}
