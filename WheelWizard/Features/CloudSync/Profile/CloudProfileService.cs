using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using WheelWizard.CloudSync.Backup;
using WheelWizard.CloudSync.Mii;
using WheelWizard.CustomDistributions;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Utilities.Generators;

namespace WheelWizard.CloudSync.Profile;

public sealed class CloudProfileService(
    IMiiProfileService miis,
    IProfileBackupService backups,
    ICustomDistributionSingletonService distributions,
    ISettingsManager settings
) : ICloudProfileService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<CloudProfilePackage> CaptureProfileAsync()
    {
        var rksysPath = distributions.RetroRewind.FindExistingRksysPath();
        if (rksysPath is null || !File.Exists(rksysPath))
            throw new FileNotFoundException("RetroWFC rksys.dat was not found by the existing Retro Rewind save locator.");

        var profileId = GetOrCreateProfileId();
        var deviceId = Guid.TryParse(settings.Get<string>(settings.CLOUD_DEVICE_ID), out var configuredDevice)
            ? configuredDevice
            : Guid.Empty;
        var nand = PathManager.GetActiveNandPath();
        var rksys = await File.ReadAllBytesAsync(rksysPath);
        var extractedMii = await miis.ExtractProfileMiiAsync(rksys);
        var licensePreviews = ReadLicensePreviews(rksys);
        var focusedSlot = Math.Clamp(settings.Get<int>(settings.FOCUSED_USER), 0, 3);
        var package = new CloudProfilePackage
        {
            ProfileId = profileId,
            ProfileName = licensePreviews[focusedSlot].Name,
            RksysData = rksys,
            MiiData = extractedMii.Data,
            MiiMissingOnSource = extractedMii.Identifier is not null && extractedMii.Data is null,
            LicensePreviews = licensePreviews,
            RrRatingData = ReadOptional(PathManager.GetRetroRewindRatingPath(nand)),
            RrSettingsData = ReadOptional(PathManager.GetRetroRewindSettingsPath(nand)),
            RrGameSettingsData = ReadOptional(PathManager.GetRetroRewindGameSettingsPath(nand)),
            GhostData = ReadGhosts(),
            Manifest = new CloudProfileManifest
            {
                ProfileId = profileId,
                LastDeviceId = deviceId,
                LastModifiedUtc = DateTime.UtcNow,
            },
        };
        package.Manifest = CreateManifest(package, profileId, deviceId, 0, []);
        return package;
    }

    public async Task ApplyProfileAsync(CloudProfilePackage profile)
    {
        await ValidateProfileAsync(profile);
        // This is deliberately inside the apply boundary, rather than left to callers to remember.
        await backups.CreateBackupAsync();

        var nand = PathManager.GetActiveNandPath();
        if (profile.MiiData is not null)
            await miis.EnsureMiiPresentAsync(profile.MiiData);

        var rksysPath = distributions.RetroRewind.FindExistingRksysPath() ?? PathManager.GetRetroWfcSavePath();
        WriteAtomic(rksysPath, profile.RksysData);
        WriteOptional(PathManager.GetRetroRewindRatingPath(nand), profile.RrRatingData);
        WriteOptional(PathManager.GetRetroRewindSettingsPath(nand), profile.RrSettingsData);
        WriteOptional(PathManager.GetRetroRewindGameSettingsPath(nand), profile.RrGameSettingsData);
        foreach (var (relativePath, data) in profile.GhostData)
        {
            if (!IsSafeRelativePath(relativePath))
                throw new InvalidDataException("Cloud package contains an unsafe ghost path.");
            WriteAtomic(Path.Combine(PathManager.RetroRewind6FolderPath, "Ghosts", relativePath), data);
        }
    }

    public Task ValidateProfileAsync(CloudProfilePackage profile)
    {
        if (profile.ProfileId == Guid.Empty || profile.Manifest.ProfileId != profile.ProfileId)
            throw new InvalidDataException("Cloud profile has no valid profile ID.");
        if (profile.Manifest.SchemaVersion != CloudProfileManifest.CurrentSchemaVersion || profile.RksysData.Length == 0)
            throw new InvalidDataException("Cloud profile schema or rksys.dat is invalid.");
        if (profile.MiiData is not null && profile.MiiData.Length != 74)
            throw new InvalidDataException("Cloud profile contains an invalid Mii block.");
        if (profile.MiiData is null && !profile.MiiMissingOnSource)
            throw new InvalidDataException("Cloud profile has no Mii state; refusing to apply an ambiguous license.");
        if (profile.MiiData is not null && profile.MiiMissingOnSource)
            throw new InvalidDataException("Cloud profile has contradictory Mii state.");
        if (profile.Manifest.MiiMissingOnSource != profile.MiiMissingOnSource)
            throw new InvalidDataException("Cloud profile Mii state does not match its manifest.");
        if (profile.GhostData.Keys.Any(path => !IsSafeRelativePath(path)))
            throw new InvalidDataException("Cloud profile contains an unsafe ghost path.");
        if (CloudHash.Of(BuildContentBytes(profile)) != profile.Manifest.ContentHash)
            throw new InvalidDataException("Cloud profile hash does not match its manifest.");
        return Task.CompletedTask;
    }

    public async Task WritePackageAsync(CloudProfilePackage profile, string zipPath)
    {
        await ValidateProfileAsync(profile);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        WriteEntry(archive, "MarioKart/rksys.dat", profile.RksysData);
        WriteEntry(archive, "Mii/profile.mii", profile.MiiData);
        WriteEntry(archive, "RetroRewind/RRRating.pul", profile.RrRatingData);
        WriteEntry(archive, "RetroRewind/RRSettings.pul", profile.RrSettingsData);
        WriteEntry(archive, "RetroRewind/RRGameSettings.pul", profile.RrGameSettingsData);
        foreach (var (name, data) in profile.GhostData)
            WriteEntry(archive, $"Ghosts/{name.Replace('\\', '/')}", data);
        WriteEntry(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(profile.Manifest, JsonOptions));
    }

    public Task<CloudProfilePackage> ReadPackageAsync(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var manifest =
            JsonSerializer.Deserialize<CloudProfileManifest>(ReadRequired(archive, "manifest.json"), JsonOptions)
            ?? throw new InvalidDataException("Cloud package manifest is missing.");
        var package = new CloudProfilePackage
        {
            ProfileId = manifest.ProfileId,
            ProfileName = manifest.ProfileName,
            RksysData = ReadRequired(archive, "MarioKart/rksys.dat"),
            MiiData = ReadOptional(archive, "Mii/profile.mii"),
            MiiMissingOnSource = manifest.MiiMissingOnSource,
            LicensePreviews = manifest.LicensePreviews,
            RrRatingData = ReadOptional(archive, "RetroRewind/RRRating.pul"),
            RrSettingsData = ReadOptional(archive, "RetroRewind/RRSettings.pul"),
            RrGameSettingsData = ReadOptional(archive, "RetroRewind/RRGameSettings.pul"),
            GhostData = ReadGhostEntries(archive),
            Manifest = manifest,
        };
        return Task.FromResult(package);
    }

    public static CloudProfileManifest CreateManifest(
        CloudProfilePackage package,
        Guid profileId,
        Guid deviceId,
        long revision,
        List<CloudDeviceRecord> devices
    )
    {
        var files = BuildFileEntries(package);
        return new CloudProfileManifest
        {
            ProfileId = profileId,
            ProfileName = package.ProfileName,
            Revision = revision,
            LastDeviceId = deviceId,
            LastModifiedUtc = DateTime.UtcNow,
            ContentHash = CloudHash.Of(BuildContentBytes(package)),
            MiiMissingOnSource = package.MiiMissingOnSource,
            MiiPreviewData = package.MiiData,
            LicensePreviews = package.LicensePreviews.ToList(),
            Devices = devices,
            Files = files,
        };
    }

    private static byte[] BuildContentBytes(CloudProfilePackage package) =>
        BuildFileEntries(package)
            .SelectMany(entry => System.Text.Encoding.UTF8.GetBytes($"{entry.LogicalName}:{entry.Hash}\n"))
            .Concat(System.Text.Encoding.UTF8.GetBytes($"mii-missing-on-source:{package.MiiMissingOnSource}\n"))
            .ToArray();

    private static List<FileManifestEntry> BuildFileEntries(CloudProfilePackage package)
    {
        var entries = new List<FileManifestEntry>();
        Add(entries, "MarioKart/rksys.dat", package.RksysData, false);
        Add(entries, "Mii/profile.mii", package.MiiData, true);
        Add(entries, "RetroRewind/RRRating.pul", package.RrRatingData, true);
        Add(entries, "RetroRewind/RRSettings.pul", package.RrSettingsData, true);
        Add(entries, "RetroRewind/RRGameSettings.pul", package.RrGameSettingsData, true);
        foreach (var (name, data) in package.GhostData.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            Add(entries, $"Ghosts/{name}", data, true);
        return entries;
    }

    private static void Add(List<FileManifestEntry> entries, string name, byte[]? data, bool optional)
    {
        if (data is not null)
            entries.Add(new FileManifestEntry(name, CloudHash.Of(data), data.LongLength, optional));
    }

    private static byte[]? ReadOptional(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;

    private static IReadOnlyDictionary<string, byte[]> ReadGhosts()
    {
        var root = Path.Combine(PathManager.RetroRewind6FolderPath, "Ghosts");
        return !Directory.Exists(root)
            ? new Dictionary<string, byte[]>()
            : Directory
                .GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(file => Path.GetRelativePath(root, file), File.ReadAllBytes);
    }

    private static IReadOnlyList<CloudLicensePreview> ReadLicensePreviews(byte[] rksys)
    {
        const int rksysHeaderSize = 0x08;
        const int rkpdSize = 0x8CC0;
        const int miiLicenseNameOffset = 0x14;
        const int profileIdOffset = 0x5C;
        const int vrOffset = 0xB0;
        const int brOffset = 0xB2;
        const int licenseNameLength = 10;
        const string rkpdMagic = "RKPD";

        var previews = new List<CloudLicensePreview>(4);
        for (var slot = 0; slot < 4; slot++)
        {
            var offset = rksysHeaderSize + slot * rkpdSize;
            var present = rksys.Length >= offset + rkpdSize && Encoding.ASCII.GetString(rksys, offset, rkpdMagic.Length) == rkpdMagic;
            if (!present)
            {
                previews.Add(new CloudLicensePreview(slot, "No license", "", 0, 0, false));
                continue;
            }

            var name = Encoding
                .BigEndianUnicode.GetString(rksys, offset + miiLicenseNameOffset, licenseNameLength * sizeof(char))
                .TrimEnd('\0')
                .Trim();
            var profileId = BinaryPrimitives.ReadUInt32BigEndian(rksys.AsSpan(offset + profileIdOffset, sizeof(uint)));
            previews.Add(
                new CloudLicensePreview(
                    slot,
                    string.IsNullOrWhiteSpace(name) ? $"License {slot + 1}" : name,
                    profileId == 0 ? "" : FriendCodeGenerator.GetFriendCode(rksys, offset + profileIdOffset),
                    BinaryPrimitives.ReadUInt16BigEndian(rksys.AsSpan(offset + vrOffset, sizeof(ushort))),
                    BinaryPrimitives.ReadUInt16BigEndian(rksys.AsSpan(offset + brOffset, sizeof(ushort))),
                    true
                )
            );
        }
        return previews;
    }

    private Guid GetOrCreateProfileId()
    {
        if (Guid.TryParse(settings.Get<string>(settings.CLOUD_PROFILE_ID), out var configuredId) && configuredId != Guid.Empty)
            return configuredId;

        var profileId = Guid.NewGuid();
        settings.Set(settings.CLOUD_PROFILE_ID, profileId.ToString("D"));
        return profileId;
    }

    private static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".cloudsync.tmp";
        File.WriteAllBytes(temporary, data);
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteOptional(string path, byte[]? data)
    {
        if (data is not null)
            WriteAtomic(path, data);
    }

    private static void WriteEntry(ZipArchive archive, string path, byte[]? data)
    {
        if (data is null)
            return;
        using var stream = archive.CreateEntry(path, CompressionLevel.Optimal).Open();
        stream.Write(data);
    }

    private static byte[] ReadRequired(ZipArchive archive, string name) =>
        ReadOptional(archive, name) ?? throw new InvalidDataException($"Cloud package is missing {name}.");

    private static byte[]? ReadOptional(ZipArchive archive, string name) => archive.GetEntry(name) is { } entry ? ReadEntry(entry) : null;

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IReadOnlyDictionary<string, byte[]> ReadGhostEntries(ZipArchive archive)
    {
        var entries = archive.Entries.Where(entry =>
            entry.FullName.StartsWith("Ghosts/", StringComparison.Ordinal) && !entry.FullName.EndsWith('/')
        );
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var relativePath = entry.FullName[7..];
            if (!IsSafeRelativePath(relativePath) || !result.TryAdd(relativePath, ReadEntry(entry)))
                throw new InvalidDataException("Cloud package contains an unsafe or duplicate ghost path.");
        }
        return result;
    }

    private static bool IsSafeRelativePath(string path) =>
        !Path.IsPathRooted(path)
        && path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).All(segment => segment is not "." and not "..");
}
