using System.Text.Json;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.CloudSync.ProfileLibrary;

public sealed class CloudProfileLibraryService(
    IGameLicenseSingletonService gameLicenses,
    ICloudSyncService cloudSync,
    ISettingsManager settings,
    IVirtualProfileVaultService vault,
    IProfileCloudBindingService bindings
) : ICloudProfileLibraryService
{
    public async Task<IReadOnlyList<ProfileLibraryEntry>> GetAllAsync()
    {
        var result = new List<ProfileLibraryEntry>();
        var localUpdatedUtc = File.Exists(PathManager.GetRetroWfcSavePath())
            ? File.GetLastWriteTimeUtc(PathManager.GetRetroWfcSavePath())
            : (DateTime?)null;
        foreach (var (license, slot) in gameLicenses.LicenseCollection.Users.Select((license, slot) => (license, slot)))
        {
            if (!IsUsableLocalLicense(license))
                continue;
            result.Add(
                new ProfileLibraryEntry(
                    $"local:{slot}",
                    ProfileLibrarySource.Local,
                    ProfileStorageState.LocalOnly,
                    license.NameOfMii,
                    license.FriendCode,
                    license.Mii,
                    slot,
                    license.Vr,
                    license.Br,
                    localUpdatedUtc
                )
            );
        }

        foreach (var virtualProfile in await vault.GetAllAsync())
        {
            var miiResult = MiiSerializer.Deserialize(virtualProfile.MiiData);
            result.Add(
                new ProfileLibraryEntry(
                    $"vault:{virtualProfile.ProfileId:D}",
                    ProfileLibrarySource.Vault,
                    ProfileStorageState.LocalOnly,
                    virtualProfile.Name,
                    virtualProfile.FriendCode,
                    miiResult.IsSuccess ? miiResult.Value : null,
                    null,
                    virtualProfile.Vr,
                    virtualProfile.Br,
                    virtualProfile.UpdatedUtc
                )
            );
        }

        if (!settings.Get<bool>(settings.CLOUD_SYNC_ENABLED))
            return result;

        try
        {
            var cloudProfiles = await cloudSync.GetAvailableProfilesAsync();
            var displayedCloudIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in cloudProfiles)
            {
                var license = SelectProfileLicensePreview(profile);
                var miiResult = MiiSerializer.Deserialize(profile.MiiPreviewData);
                var cloudMii = miiResult.IsSuccess ? miiResult.Value : null;
                var cloudIdentity = GetCloudIdentity(profile.ProfileName, license?.FriendCode, cloudMii?.MiiId);

                // GetAvailableProfilesAsync orders newest first. Multiple records can exist after a profile
                // was synced before profile bindings were introduced, so retain the newest one and never make
                // the same Mario Kart license look like several cloud-only profiles.
                if (cloudIdentity is not null && !displayedCloudIdentities.Add(cloudIdentity))
                    continue;

                var matchingLocalIndex = FindMatchingLocalProfile(
                    result,
                    profile.ProfileName,
                    [license?.FriendCode ?? string.Empty],
                    cloudMii?.MiiId
                );
                if (matchingLocalIndex >= 0)
                {
                    // Keep the local key and slot: choosing this card must continue to select the
                    // existing physical license when WiiCompiled is launched.
                    result[matchingLocalIndex] = result[matchingLocalIndex] with
                    {
                        StorageState = ProfileStorageState.CloudAndLocal,
                        LastUpdatedUtc = Max(result[matchingLocalIndex].LastUpdatedUtc, profile.LastModifiedUtc),
                    };
                    // A visual local/cloud match must also establish the canonical sync identity.
                    // Otherwise a later pull would create a random ID for this physical slot and
                    // leave the actual remote profile untouched.
                    if (result[matchingLocalIndex].LocalSlot is int localSlot)
                        await bindings.BindAsync(localSlot, profile.ProfileId);
                    continue;
                }

                result.Add(
                    new ProfileLibraryEntry(
                        $"cloud:{profile.ProfileId:D}",
                        ProfileLibrarySource.Cloud,
                        ProfileStorageState.CloudOnly,
                        profile.ProfileName,
                        license?.FriendCode ?? string.Empty,
                        cloudMii,
                        null,
                        license?.Vr ?? 0,
                        license?.Br ?? 0,
                        profile.LastModifiedUtc
                    )
                );
            }
        }
        catch
        {
            // The local library remains usable if the provider is disconnected or temporarily unavailable.
        }

        return result;
    }

    private static CloudLicensePreview? SelectProfileLicensePreview(CloudProfileManifest profile)
    {
        var matchingNamePreviews = profile.LicensePreviews
            .Where(preview => preview.IsPresent && string.Equals(preview.Name, profile.ProfileName, StringComparison.Ordinal))
            .ToList();
        return matchingNamePreviews.Count == 1
            ? matchingNamePreviews[0]
            : profile.LicensePreviews.FirstOrDefault(preview => preview.IsPresent);
    }

    private static int FindMatchingLocalProfile(
        IReadOnlyList<ProfileLibraryEntry> localProfiles,
        string cloudProfileName,
        IEnumerable<string> cloudFriendCodes,
        uint? cloudMiiId
    )
    {
        var localIndexes = Enumerable
            .Range(0, localProfiles.Count)
            .Where(index =>
                localProfiles[index].Source is ProfileLibrarySource.Local or ProfileLibrarySource.Vault
                && localProfiles[index].StorageState == ProfileStorageState.LocalOnly
            )
            .ToList();

        // The cloud Mii preview belongs to the focused profile, so it is the strongest identity when present.
        var miiMatches =
            cloudMiiId is > 0
                ? localIndexes.Where(index => localProfiles[index].Mii?.MiiId == cloudMiiId).ToList()
                : [];
        if (miiMatches.Count == 1)
            return miiMatches[0];

        // A standard cloud package contains the entire rksys.dat, not only the focused profile. Check every
        // license preview instead of assuming the first present slot is the profile represented by the card.
        var normalizedCloudFriendCodes = cloudFriendCodes.Select(NormalizeFriendCode).Where(code => code is not null).ToHashSet();
        var friendCodeMatches = localIndexes
            .Where(index => normalizedCloudFriendCodes.Contains(NormalizeFriendCode(localProfiles[index].FriendCode)))
            .ToList();
        if (friendCodeMatches.Count == 1)
            return friendCodeMatches[0];

        // Offline profiles have neither a usable friend code nor a reliably persisted Mii. A unique name is a
        // safe fallback; when several local profiles share a name, leave the cloud entry separate rather than
        // potentially merging two distinct licenses.
        var nameMatches = localIndexes
            .Where(index => string.Equals(localProfiles[index].Name, cloudProfileName, StringComparison.Ordinal))
            .ToList();
        return nameMatches.Count == 1 ? nameMatches[0] : -1;
    }

    private static string? NormalizeFriendCode(string? friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return null;

        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        return digits.Length == 12 && digits != "000000000000" ? digits : null;
    }

    private static string? GetCloudIdentity(string profileName, string? friendCode, uint? miiId)
    {
        var normalizedFriendCode = NormalizeFriendCode(friendCode);
        if (normalizedFriendCode is not null)
            return $"friend:{normalizedFriendCode}";
        if (miiId is > 0)
            return $"mii:{miiId}";

        return string.IsNullOrWhiteSpace(profileName) ? null : $"name:{profileName.Trim()}";
    }

    /// <summary>GameLicenseService represents an empty RKPD slot as a dummy Mii named "no license".</summary>
    private static bool IsUsableLocalLicense(LicenseProfile license) =>
        license.Mii is not null
        && !string.IsNullOrWhiteSpace(license.NameOfMii)
        && !string.Equals(license.NameOfMii, SettingValues.NoLicense, StringComparison.OrdinalIgnoreCase);

    private static DateTime? Max(DateTime? first, DateTime second) => first is null || second > first.Value ? second : first;

    public IReadOnlyList<ProfileLibraryEntry> GetVisible(IReadOnlyList<ProfileLibraryEntry> profiles)
    {
        var keys = ReadVisibleKeys();
        if (keys.Count == 0)
            return profiles.Where(profile => profile.Source is ProfileLibrarySource.Local or ProfileLibrarySource.Vault).Take(4).ToList();

        return keys.Select(key => profiles.FirstOrDefault(profile => profile.Key == key))
            .Where(profile => profile is not null)
            .Take(4)
            .Cast<ProfileLibraryEntry>()
            .ToList();
    }

    public void SaveVisible(IEnumerable<string> profileKeys)
    {
        var keys = profileKeys.Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).Take(4).ToList();
        settings.Set(settings.CLOUD_VISIBLE_PROFILE_IDS, JsonSerializer.Serialize(keys));
    }

    public IReadOnlyList<ProfileLibraryEntry> GetSyncSelected(IReadOnlyList<ProfileLibraryEntry> profiles)
    {
        var storedValue = settings.Get<string>(settings.CLOUD_SYNC_PROFILE_IDS);
        if (string.IsNullOrWhiteSpace(storedValue))
            return profiles.Where(profile => profile.Source is ProfileLibrarySource.Local or ProfileLibrarySource.Vault).ToList();

        var keys = ReadKeys(storedValue);
        return keys.Select(key =>
                profiles.FirstOrDefault(profile =>
                    profile.Key == key && profile.Source is ProfileLibrarySource.Local or ProfileLibrarySource.Vault
                )
            )
            .Where(profile => profile is not null)
            .Cast<ProfileLibraryEntry>()
            .ToList();
    }

    public void SaveSyncSelected(IEnumerable<string> profileKeys)
    {
        var keys = profileKeys
            .Where(key => key.StartsWith("local:", StringComparison.Ordinal) || key.StartsWith("vault:", StringComparison.Ordinal))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        settings.Set(settings.CLOUD_SYNC_PROFILE_IDS, JsonSerializer.Serialize(keys));
    }

    private IReadOnlyList<string> ReadVisibleKeys()
    {
        try
        {
            return ReadKeys(settings.Get<string>(settings.CLOUD_VISIBLE_PROFILE_IDS));
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ReadKeys(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
