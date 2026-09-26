using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using WheelWizard.CloudSync.Mii;
using WheelWizard.Services;
using WheelWizard.Utilities.Generators;

namespace WheelWizard.CloudSync.ProfileLibrary;

public sealed class VirtualProfileVaultService(IMiiProfileService miis) : IVirtualProfileVaultService
{
    private const int RkpdSize = 0x8CC0;
    private const int MiiNameOffset = 0x14;
    private const int AvatarIdOffset = 0x28;
    private const int ProfileIdOffset = 0x5C;
    private const int VrOffset = 0xB0;
    private const int BrOffset = 0xB2;
    private const int LicenseNameLength = 10;

    private string Folder => Path.Combine(PathManager.CloudSyncStateFolderPath, "virtual-profiles");

    private string PathFor(Guid profileId) => Path.Combine(Folder, $"{profileId:D}.json");

    public async Task<IReadOnlyList<VirtualProfileRecord>> GetAllAsync()
    {
        if (!Directory.Exists(Folder))
            return [];

        var profiles = new List<VirtualProfileRecord>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<VirtualProfileRecord>(await File.ReadAllTextAsync(path));
                if (profile is not null && !profile.IsLocalMirror && IsValidLicense(profile.LicenseData))
                    profiles.Add(profile);
            }
            catch (JsonException)
            {
                // A malformed vault item is ignored rather than making the rest of the library unavailable.
            }
        }

        return profiles.OrderByDescending(profile => profile.UpdatedUtc).ToList();
    }

    public async Task<VirtualProfileRecord?> GetAsync(Guid profileId)
    {
        var path = PathFor(profileId);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<VirtualProfileRecord>(await File.ReadAllTextAsync(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<VirtualProfileRecord> CreateAsync(byte[] licenseData)
    {
        var profile = await BuildAsync(Guid.NewGuid(), licenseData);
        await WriteAsync(profile);
        return profile;
    }

    public async Task UpdateAsync(Guid profileId, byte[] licenseData)
    {
        var profile = await BuildAsync(profileId, licenseData);
        await WriteAsync(profile);
    }

    public async Task ImportAsync(Guid profileId, byte[] licenseData, byte[]? miiData, bool isLocalMirror = false)
    {
        if (miiData is not null)
            await miis.EnsureMiiPresentAsync(miiData);

        var profile = await BuildAsync(profileId, licenseData, miiData, isLocalMirror);
        await WriteAsync(profile);
    }

    public Task EnsureMiiPresentAsync(VirtualProfileRecord profile) =>
        profile.MiiData is null ? Task.CompletedTask : miis.EnsureMiiPresentAsync(profile.MiiData);

    private async Task<VirtualProfileRecord> BuildAsync(
        Guid profileId,
        byte[] licenseData,
        byte[]? miiOverride = null,
        bool isLocalMirror = false
    )
    {
        if (!IsValidLicense(licenseData))
            throw new InvalidDataException("A virtual profile does not contain a valid Mario Kart license block.");

        var avatarId = BinaryPrimitives.ReadUInt32BigEndian(licenseData.AsSpan(AvatarIdOffset, sizeof(uint)));
        byte[]? mii = miiOverride;
        if (mii is null && avatarId != 0)
        {
            try
            {
                mii = await miis.ExtractMiiAsync(new MiiIdentifier(avatarId));
            }
            catch (InvalidDataException)
            {
                // Keep the missing-Mii state explicit; never substitute another local Mii.
            }
        }

        var name = Encoding.BigEndianUnicode.GetString(licenseData, MiiNameOffset, LicenseNameLength * sizeof(char)).TrimEnd('\0').Trim();
        var pid = BinaryPrimitives.ReadUInt32BigEndian(licenseData.AsSpan(ProfileIdOffset, sizeof(uint)));
        return new VirtualProfileRecord(
            profileId,
            licenseData.ToArray(),
            mii,
            string.IsNullOrWhiteSpace(name) ? "Mario Kart license" : name,
            pid == 0 ? string.Empty : FriendCodeGenerator.GetFriendCode(licenseData, ProfileIdOffset),
            BinaryPrimitives.ReadUInt16BigEndian(licenseData.AsSpan(VrOffset, sizeof(ushort))),
            BinaryPrimitives.ReadUInt16BigEndian(licenseData.AsSpan(BrOffset, sizeof(ushort))),
            DateTime.UtcNow,
            isLocalMirror
        );
    }

    private async Task WriteAsync(VirtualProfileRecord profile)
    {
        Directory.CreateDirectory(Folder);
        var path = PathFor(profile.ProfileId);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(profile));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsValidLicense(byte[] data) => data.Length == RkpdSize && data.AsSpan(0, 4).SequenceEqual("RKPD"u8);
}
