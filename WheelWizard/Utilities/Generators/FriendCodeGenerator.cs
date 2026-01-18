using System.Security.Cryptography;
using System.Text;
using WheelWizard.Helpers;

namespace WheelWizard.Utilities.Generators;

public class FriendCodeGenerator
{
    private const uint GameCodeInt = 0x524D434A; // "RMCJ" in big-endian

    public static string GetFriendCode(byte[] data, int offset)
    {
        var pid = BigEndianBinaryHelper.BufferToUint32(data, offset);
        if (pid == 0)
            return string.Empty;

        var srcBuf = new byte[] { data[offset + 3], data[offset + 2], data[offset + 1], data[offset + 0], 0x4A, 0x43, 0x4D, 0x52 };
        var md5Hash = GetMd5Hash(srcBuf);
        var fcDec = ((Convert.ToUInt32(md5Hash.Substring(0, 2), 16) >> 1) * (1UL << 32)) + pid;

        return FormatFriendCode(fcDec);
    }

    /// <summary>
    /// Converts a profile ID to a friend code.
    /// </summary>
    public static ulong ProfileIdToFriendCode(uint profileId)
    {
        if (profileId == 0)
            return 0;

        // Byte swap both values (same as Python _bswap32)
        var swappedProfileId = BSwap32(profileId);
        var swappedGameCode = BSwap32(GameCodeInt);

        // Pack as big-endian (">II" format): [swapped_profile_id_bytes] + [swapped_game_code_bytes]
        var data = new byte[8];
        // Write swapped profile ID as big-endian
        data[0] = (byte)(swappedProfileId >> 24);
        data[1] = (byte)(swappedProfileId >> 16);
        data[2] = (byte)(swappedProfileId >> 8);
        data[3] = (byte)swappedProfileId;
        // Write swapped game code as big-endian
        data[4] = (byte)(swappedGameCode >> 24);
        data[5] = (byte)(swappedGameCode >> 16);
        data[6] = (byte)(swappedGameCode >> 8);
        data[7] = (byte)swappedGameCode;

        // Calculate MD5 and get checksum (first byte >> 1)
        using var md5 = MD5.Create();
        var hashBytes = md5.ComputeHash(data);
        var checksum = (uint)(hashBytes[0] >> 1);

        // Return friend code: (checksum << 32) | profileId
        return ((ulong)checksum << 32) | profileId;
    }

    /// <summary>
    /// Converts a friend code string (format: "XXXX-XXXX-XXXX") to a profile ID.
    /// </summary>
    public static uint FriendCodeToProfileId(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return 0;

        // Remove dashes and parse as ulong
        var fcString = friendCode.Replace("-", "");
        if (fcString.Length != 12 || !ulong.TryParse(fcString, out var fcDec))
            return 0;

        // Extract profile ID (lower 32 bits)
        var profileId = (uint)(fcDec & 0xFFFFFFFF);
        if (profileId == 0)
            return 0;

        // Verify checksum
        var expectedFc = ProfileIdToFriendCode(profileId);
        var expectedChecksum = (uint)(expectedFc >> 32);
        var actualChecksum = (uint)(fcDec >> 32);

        // Allow checksum to be 0 or match expected
        if (actualChecksum != 0 && actualChecksum != expectedChecksum)
            return 0;

        return profileId;
    }

    private static string GetMd5Hash(byte[] input)
    {
        using (var md5 = MD5.Create())
        {
            var hashBytes = md5.ComputeHash(input);
            var sb = new StringBuilder();
            foreach (var t in hashBytes)
            {
                sb.Append(t.ToString("x2"));
            }

            return sb.ToString();
        }
    }

    private static string FormatFriendCode(ulong fcDec)
    {
        var fc = "";
        for (var i = 0; i < 3; i++)
        {
            fc += FcPartParse((int)(fcDec / Math.Pow(10, 4 * (2 - i)) % 10000));
            if (i < 2)
                fc += "-";
        }

        return fc;
    }

    private static string FcPartParse(int part) => part.ToString("D4");

    private static uint BSwap32(uint value)
    {
        return ((value & 0xFF000000) >> 24) | ((value & 0x00FF0000) >> 8) | ((value & 0x0000FF00) << 8) | ((value & 0x000000FF) << 24);
    }
}
