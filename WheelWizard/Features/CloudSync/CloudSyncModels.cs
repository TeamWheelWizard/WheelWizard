using System.Security.Cryptography;

namespace WheelWizard.CloudSync;

public enum CloudProviderType
{
    WebDav,
    Nextcloud,
    GoogleDrive,
    OneDrive,
}

public enum EnrollmentState
{
    Pending,
    Verified,
    Blocked,
}

public enum CloudSyncAction
{
    Disabled,
    NoOp,
    Pulled,
    Pushed,
    PendingEnrollment,
    Conflict,
    Failed,
}

public enum ConflictResolutionStrategy
{
    UseLocal,
    UseRemote,
    PreserveBoth,
    Cancel,
}

public enum ConflictKind
{
    NoOp,
    SafePush,
    SafePull,
    Conflict,
}

public sealed class CloudProfilePackage
{
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = "Mario Kart Wii";
    public required byte[] RksysData { get; init; }
    public byte[]? MiiData { get; init; }

    /// <summary>
    /// The source save references a Mii which is not available in its local RFL_DB.dat.
    /// This is kept explicit so a receiving device never substitutes an unrelated Mii.
    /// </summary>
    public bool MiiMissingOnSource { get; init; }
    public IReadOnlyList<CloudLicensePreview> LicensePreviews { get; init; } = [];
    public byte[]? RrRatingData { get; init; }
    public byte[]? RrSettingsData { get; init; }
    public byte[]? RrGameSettingsData { get; init; }
    public IReadOnlyDictionary<string, byte[]> GhostData { get; init; } = new Dictionary<string, byte[]>();
    public required CloudProfileManifest Manifest { get; set; }
}

public sealed class CloudProfileManifest
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public Guid ProfileId { get; init; }
    public string ProfileName { get; init; } = "Mario Kart Wii";
    public long Revision { get; init; }
    public DateTime LastModifiedUtc { get; init; }
    public Guid LastDeviceId { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public bool MiiMissingOnSource { get; init; }

    /// <summary>Optional 74-byte Mii preview for displaying the selected license in a picker.</summary>
    public byte[]? MiiPreviewData { get; init; }
    public List<CloudLicensePreview> LicensePreviews { get; init; } = [];
    public List<CloudDeviceRecord> Devices { get; init; } = [];
    public List<FileManifestEntry> Files { get; init; } = [];
}

public sealed record CloudDeviceRecord(
    Guid DeviceId,
    string DeviceName,
    string Platform,
    DateTime FirstSeenUtc,
    DateTime? LastPlayedUtc,
    EnrollmentState EnrollmentState
);

public sealed record FileManifestEntry(string LogicalName, string Hash, long Size, bool Optional);

public sealed record CloudProviderConfig(CloudProviderType ProviderType, string DisplayName, string RemoteRoot);

public sealed record MiiIdentifier(uint Value);

/// <summary>Non-sensitive metadata used to choose a cloud profile before downloading it.</summary>
public sealed record CloudLicensePreview(int Slot, string Name, string FriendCode, uint Vr, uint Br, bool IsPresent);

public sealed record ProfileMiiExtraction(MiiIdentifier? Identifier, byte[]? Data);

public sealed record BackupInfo(string FolderPath, DateTime CreatedUtc);

public sealed record CloudSyncResult(bool Success, CloudSyncAction Action, string Message, ConflictKind? Conflict = null)
{
    public static CloudSyncResult Ok(CloudSyncAction action, string message) => new(true, action, message);

    public static CloudSyncResult Fail(string message) => new(false, CloudSyncAction.Failed, message);
}

public sealed record CloudSyncStatus(
    bool Enabled,
    string Provider,
    Guid? ProfileId,
    Guid DeviceId,
    EnrollmentState EnrollmentState,
    string Message
);

public sealed record ConflictResult(ConflictKind Kind, string Message);

public sealed record ResolutionResult(bool Applied, string Message);

public sealed record ProbeResult(bool Successful, bool IsIdentityLinkError, string Message);

internal sealed record CloudSyncLocalState
{
    public long LastKnownCloudRevision { get; init; }
    public string LastKnownCloudHash { get; init; } = string.Empty;
    public string LastLocalHash { get; init; } = string.Empty;
    public EnrollmentState EnrollmentState { get; init; } = EnrollmentState.Pending;
}

internal static class CloudHash
{
    public static string Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
