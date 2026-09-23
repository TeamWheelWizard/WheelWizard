using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.CloudSync.ProfileLibrary;

/// <summary>
/// Stores additional local licences outside the four physical Mario Kart Wii slots.  Records are
/// materialised into a temporary rksys.dat view only while WiiCompiled is running.
/// </summary>
public sealed record VirtualProfileRecord(
    Guid ProfileId,
    byte[] LicenseData,
    byte[]? MiiData,
    string Name,
    string FriendCode,
    uint Vr,
    uint Br,
    DateTime UpdatedUtc,
    bool IsLocalMirror = false
);

public interface IVirtualProfileVaultService
{
    Task<IReadOnlyList<VirtualProfileRecord>> GetAllAsync();
    Task<VirtualProfileRecord?> GetAsync(Guid profileId);
    Task<VirtualProfileRecord> CreateAsync(byte[] licenseData);
    Task UpdateAsync(Guid profileId, byte[] licenseData);

    /// <summary>Imports a cloud profile without assigning it to one of the four physical save slots.</summary>
    Task ImportAsync(Guid profileId, byte[] licenseData, byte[]? miiData, bool isLocalMirror = false);
    Task EnsureMiiPresentAsync(VirtualProfileRecord profile);
}
