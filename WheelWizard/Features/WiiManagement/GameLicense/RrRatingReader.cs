using WheelWizard.Shared.Binary;
using WheelWizard.WiiManagement.FriendCodes;

namespace WheelWizard.WiiManagement.GameLicense;

public interface IRrRatingReader
{
    /// <summary>
    /// Reads VR and BR values from RRRating.pul file for a given profile ID.
    /// </summary>
    (float vr, float br)? ReadRatingFromFile(byte[] fileData, uint profileId);

    /// <summary>
    /// Reads VR and BR values from RRRating.pul file for a given friend code.
    /// </summary>
    (float vr, float br)? ReadRatingFromFileByFriendCode(byte[] fileData, string friendCode);
}

public class RrRatingReader : IRrRatingReader
{
    private const uint Magic = 0x52525254; // 'RRRT'
    private const ushort Version = 1;
    private const int MaxProfiles = 100;
    private const int HeaderSize = 8; // IHH = 4 + 2 + 2
    private const int EntrySize = 16; // iffI = 4 + 4 + 4 + 4
    private const uint FlagHasData = 0x1;

    /// <summary>
    /// Reads VR and BR values from RRRating.pul file for a given profile ID.
    /// </summary>
    public (float vr, float br)? ReadRatingFromFile(byte[] fileData, uint profileId)
    {
        if (fileData == null || fileData.Length < HeaderSize + EntrySize * MaxProfiles)
            return null;

        // Read header
        var magic = BigEndianBinary.BufferToUint32(fileData, 0);
        var version = BigEndianBinary.BufferToUint16(fileData, 4);
        var count = BigEndianBinary.BufferToUint16(fileData, 6);

        if (magic != Magic || version != Version || count != MaxProfiles)
            return null;

        // Search for matching profile ID
        var offset = HeaderSize;
        for (var i = 0; i < MaxProfiles; i++)
        {
            var entryProfileId = BigEndianBinary.BufferToUint32(fileData, offset);
            var vr = BigEndianBinary.BufferToFloat(fileData, offset + 4);
            var br = BigEndianBinary.BufferToFloat(fileData, offset + 8);
            var flags = BigEndianBinary.BufferToUint32(fileData, offset + 12);

            var hasData = (flags & FlagHasData) != 0 && entryProfileId > 0;

            if (hasData && entryProfileId == profileId)
            {
                return (vr, br);
            }

            offset += EntrySize;
        }

        return null;
    }

    /// <summary>
    /// Reads VR and BR values from RRRating.pul file for a given friend code.
    /// </summary>
    public (float vr, float br)? ReadRatingFromFileByFriendCode(byte[] fileData, string friendCode)
    {
        var profileId = FriendCode.FriendCodeToProfileId(friendCode);
        if (profileId == 0)
            return null;

        return ReadRatingFromFile(fileData, profileId);
    }
}
