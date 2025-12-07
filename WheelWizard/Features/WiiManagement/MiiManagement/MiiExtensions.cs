using WheelWizard.Services.Settings;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement;

public static class MiiExtensions
{
    public static bool IsTheSameAs(this Mii self, Mii? other)
    {
        if (other == null)
            return false;

        var selfBytes = MiiSerializer.Serialize(self);
        if (selfBytes.IsFailure)
            return false;

        var otherBytes = MiiSerializer.Serialize(other);
        if (otherBytes.IsFailure)
            return false;

        return Convert.ToBase64String(selfBytes.Value) == Convert.ToBase64String(otherBytes.Value);
    }

    public static OperationResult<Mii> Clone(this Mii self)
    {
        // This is not the fastest way to clone, but it is the easiest way.
        var miiId = self.MiiId;
        self.MiiId = 1; // Make it 1 for the cloning process to ensure that that is not a reason for failure.
        // That being said, we still reset the ID afterward to make sure that if the og is false, then the clone is also false.

        var selfBytes = MiiSerializer.Serialize(self);
        self.MiiId = miiId; // IMPORTANT: Set the MiiId back, since you are changing the original record here, we don't want that.
        if (selfBytes.IsFailure)
            return selfBytes.Error;

        var cloneResult = MiiSerializer.Deserialize(selfBytes.Value);
        if (cloneResult.IsFailure)
            return cloneResult.Error;

        cloneResult.Value.MiiId = miiId; // IMPORTANT: Also make sure that the clone has the same id as the OG, it's a clone after all.
        return cloneResult.Value;
    }

    public static bool IsGlobal(this Mii self)
    {
        // If it has blue pants, then its definitely global
        if ((self.MiiId1 >> 5) == 0b110)
            return true;

        // But it can also be global if the mac address is not the same as your own address
        var macAddressString = (string)SettingsManager.MACADDRESS.Get();
        var macParts = macAddressString.Split(':');
        var macBytes = new byte[6];
        for (var i = 0; i < 6; i++)
            macBytes[i] = byte.Parse(macParts[i], System.Globalization.NumberStyles.HexNumber);
        var systemId0 = (byte)((macBytes[0] + macBytes[1] + macBytes[2]) & 0xFF);
        return (
            self?.SystemId0 != systemId0
            || self?.SystemId1 != macBytes[3]
            || self?.SystemId2 != macBytes[4]
            || self?.SystemId3 != macBytes[5]
        );
    }
}
