using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.CloudSync.ProfileLibrary;

public static class ProfileCloudBindingIdentity
{
    public static string Create(LicenseProfile profile) => Create(profile.FriendCode, profile.Mii?.MiiId, profile.NameOfMii);

    public static string Create(ProfileLibraryEntry profile) => Create(profile.FriendCode, profile.Mii?.MiiId, profile.Name);

    private static string Create(string? friendCode, uint? miiId, string? name)
    {
        var friendCodeDigits = string.IsNullOrWhiteSpace(friendCode) ? string.Empty : new string(friendCode.Where(char.IsDigit).ToArray());
        return $"friend:{friendCodeDigits}|mii:{miiId ?? 0}|name:{name?.Trim() ?? string.Empty}";
    }
}
