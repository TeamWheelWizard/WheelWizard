using MiiModel = WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Mii;

namespace WheelWizard.CloudSync.ProfileLibrary;

public enum ProfileLibrarySource
{
    Local,
    Vault,
    Cloud,
}

/// <summary>Where the profile can currently be used from.</summary>
public enum ProfileStorageState
{
    LocalOnly,
    CloudOnly,
    CloudAndLocal,
}

/// <summary>A display-only profile reference. Selecting it never modifies a save or applies cloud data.</summary>
public sealed record ProfileLibraryEntry(
    string Key,
    ProfileLibrarySource Source,
    ProfileStorageState StorageState,
    string Name,
    string FriendCode,
    MiiModel? Mii,
    int? LocalSlot,
    uint Vr = 0,
    uint Br = 0,
    DateTime? LastUpdatedUtc = null
);

public interface ICloudProfileLibraryService
{
    Task<IReadOnlyList<ProfileLibraryEntry>> GetAllAsync();
    IReadOnlyList<ProfileLibraryEntry> GetVisible(IReadOnlyList<ProfileLibraryEntry> profiles);
    void SaveVisible(IEnumerable<string> profileKeys);
    IReadOnlyList<ProfileLibraryEntry> GetSyncSelected(IReadOnlyList<ProfileLibraryEntry> profiles);
    void SaveSyncSelected(IEnumerable<string> profileKeys);
}
