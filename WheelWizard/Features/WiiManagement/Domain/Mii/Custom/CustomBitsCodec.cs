using System.Collections.Generic;
using System.Linq;
using WheelWizard.Helpers;

/// <summary>
/// Handles the low-level reading and writing of the 24 custom bits spread across
/// different locations within the 74-byte raw Mii data format.
/// It ensures that only the designated "unknown" and usable bits are touched, preserving
/// the official Mii data structure and avoiding "DO NOT USE" bits.
///
/// Extraction (`Extract`): Reads the relevant sections of the Mii data, isolates the specific
///                         usable unused bits, and packs them together into a single 24-bit `uint`.
/// Injection (`Inject`): Takes a 24-bit `uint` payload, unpacks it into fragments, reads the
///                       relevant Mii data sections, clears only the usable unused bits, and writes
///                       the fragments back into their original locations.
///
/// Usable bits mapping (LSB 0-indexed within their respective data chunks):
/// - 0x20–0x21 (Face Data, ushort):
///   - unknown_0 (3 bits): Bits 3,4,5. Mask 0x0038.
///   - unknown_1 (1 bit): Bit 1. Mask 0x0002.
/// - 0x22–0x23 (Hair Data, ushort):
///   - unknown_2 (5 bits): Bits 0,1,2,3,4. Mask 0x001F.
/// - 0x24–0x27 (Eyebrow Data, uint):
///   - unknown_4 (6 bits): Bits 16,17,18,19,20,21. Mask 0x003F0000.
///     (unknown_3 at bit 26 is DO NOT USE)
/// - 0x28–0x2B (Eye Data, uint):
///   - unknown_7 (5 bits): Bits 0,1,2,3,4. Mask 0x0000001F.
///     (unknown_5 at bits 24,25 and unknown_6 at bit 12 are DO NOT USE)
/// - 0x2C–0x2D (Nose Data, ushort):
///   - unknown_8 (3 bits): Bits 0,1,2. Mask 0x0007.
/// - 0x34–0x35 (Mole Data, ushort):
///   - unknown_10 (1 bit): Bit 0. Mask 0x0001.
///
/// Total usable bits: 3 + 1 + 5 + 6 + 5 + 3 + 1 = 24 bits.
/// </summary>
internal static class CustomBitsCodec
{
    // Define constants for the byte offsets within the 74-byte Mii array
    private const int OFS_FACE = 0x20; // unknown_0, unknown_1
    private const int OFS_HAIR = 0x22; // unknown_2
    private const int OFS_BROW = 0x24; // unknown_4 (unknown_3 is not used)
    private const int OFS_EYE = 0x28; // unknown_7 (unknown_5, unknown_6 are not used)
    private const int OFS_NOSE = 0x2C; // unknown_8
    private const int OFS_MOLE = 0x34; // unknown_10

    // Masks for the USABLE unknown bits within their respective data chunks (ushort or uint)
    // For Face Data (OFS_FACE):
    private const ushort MASK_U0_FACE = 0x0038; // Bits 3,4,5 for unknown_0
    private const ushort MASK_U1_FACE = 0x0002; // Bit 1 for unknown_1
    private const ushort MASK_FACE_COMBINED = MASK_U0_FACE | MASK_U1_FACE; // 0x003A

    // For Hair Data (OFS_HAIR):
    private const ushort MASK_U2_HAIR = 0x001F; // Bits 0-4 for unknown_2

    // For Eyebrow Data (OFS_BROW):
    private const uint MASK_U4_BROW = 0x003F0000; // Bits 16-21 for unknown_4

    // For Eye Data (OFS_EYE):
    private const uint MASK_U7_EYE = 0x0000001F; // Bits 0-4 for unknown_7

    // For Nose Data (OFS_NOSE):
    private const ushort MASK_U8_NOSE = 0x0007; // Bits 0-2 for unknown_8

    // For Mole Data (OFS_MOLE):
    private const ushort MASK_U10_MOLE = 0x0001; // Bit 0 for unknown_10

    // Defines the width (in bits) of each USABLE unknown fragment, in the order they are packed.
    // This MUST match the order of operations in Extract() and Inject().
    // Total width must sum to 24 bits.
    // Order: U0, U1, U2, U4, U7, U8, U10
    private static readonly int[] _widths = { 3, 1, 5, 6, 5, 3, 1 }; // Sum = 24

    /// <summary>
    /// Extracts the 24 designated usable unused bits from the raw Mii data and packs them
    /// into a single right-aligned 24-bit unsigned integer.
    /// </summary>
    /// <param name="rawMiiBytes">The 74-byte array containing Mii data.</param>
    internal static uint Extract(byte[] rawMiiBytes)
    {
        var faceData = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_FACE);
        var hairData = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_HAIR);
        var browData = BigEndianBinaryHelper.BufferToUint32(rawMiiBytes, OFS_BROW);
        var eyeData = BigEndianBinaryHelper.BufferToUint32(rawMiiBytes, OFS_EYE);
        var noseData = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_NOSE);
        var moleData = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_MOLE);

        uint payload = 0;
        var cursor = 0; // Tracks the next bit position to write into the payload (LSB).

        // Fragment 1: unknown_0 (3 bits from faceData, LSB bits 3-5)
        var frag_u0 = (uint)((faceData & MASK_U0_FACE) >> 3);
        WriteFragment(ref payload, ref cursor, frag_u0, _widths[0]); // 3 bits

        // Fragment 2: unknown_1 (1 bit from faceData, LSB bit 1)
        var frag_u1 = (uint)((faceData & MASK_U1_FACE) >> 1);
        WriteFragment(ref payload, ref cursor, frag_u1, _widths[1]); // 1 bit

        // Fragment 3: unknown_2 (5 bits from hairData, LSB bits 0-4)
        var frag_u2 = (uint)(hairData & MASK_U2_HAIR);
        WriteFragment(ref payload, ref cursor, frag_u2, _widths[2]); // 5 bits

        // Fragment 4: unknown_4 (6 bits from browData, LSB bits 16-21)
        // (unknown_3 is skipped as it's DO NOT USE)
        var frag_u4 = (browData & MASK_U4_BROW) >> 16;
        WriteFragment(ref payload, ref cursor, frag_u4, _widths[3]); // 6 bits

        // Fragment 5: unknown_7 (5 bits from eyeData, LSB bits 0-4)
        // (unknown_5 and unknown_6 are skipped as they are DO NOT USE)
        var frag_u7 = eyeData & MASK_U7_EYE;
        WriteFragment(ref payload, ref cursor, frag_u7, _widths[4]); // 5 bits

        // Fragment 6: unknown_8 (3 bits from noseData, LSB bits 0-2)
        var frag_u8 = (uint)(noseData & MASK_U8_NOSE);
        WriteFragment(ref payload, ref cursor, frag_u8, _widths[5]); // 3 bits

        // Fragment 7: unknown_10 (1 bit from moleData, LSB bit 0)
        var frag_u10 = (uint)(moleData & MASK_U10_MOLE);
        WriteFragment(ref payload, ref cursor, frag_u10, _widths[6]); // 1 bit

        return payload;
    }

    /// <summary>
    /// Injects a 24-bit custom data payload back into the designated usable unused bit locations
    /// within a raw 74-byte Mii data array. Modifies the array in place.
    /// Only bits corresponding to U0, U1, U2, U4, U7, U8, U10 are modified.
    /// </summary>
    /// <param name="rawMiiBytes">The 74-byte array representing Mii data. This array will be modified.</param>
    /// <param name="payload">The 24-bit custom data payload (stored in the lower bits of the uint).</param>
    internal static void Inject(byte[] rawMiiBytes, uint payload)
    {
        var fragments = Decompose(payload).ToArray();
        var fragIndex = 0;

        // Face Data (U0, U1)
        var faceVal = (ushort)BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_FACE);
        faceVal = (ushort)(
            (faceVal & ~MASK_FACE_COMBINED) // Clear U0 and U1 bits
            | (fragments[fragIndex++] << 3) // Write U0 (3 bits) to LSB bits 3,4,5
            | (fragments[fragIndex++] << 1)
        ); // Write U1 (1 bit) to LSB bit 1
        BigEndianBinaryHelper.WriteUInt16BigEndian(rawMiiBytes, OFS_FACE, faceVal);

        // Hair Data (U2)
        var hairVal = (ushort)BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_HAIR);
        hairVal = (ushort)(
            (hairVal & ~MASK_U2_HAIR) // Clear U2 bits
            | fragments[fragIndex++]
        ); // Write U2 (5 bits) to LSB bits 0-4
        BigEndianBinaryHelper.WriteUInt16BigEndian(rawMiiBytes, OFS_HAIR, hairVal);

        // Eyebrow Data (U4) - unknown_3 is NOT touched
        var browVal = BigEndianBinaryHelper.BufferToUint32(rawMiiBytes, OFS_BROW);
        browVal =
            (browVal & ~MASK_U4_BROW) // Clear U4 bits
            | (fragments[fragIndex++] << 16); // Write U4 (6 bits) to LSB bits 16-21
        BigEndianBinaryHelper.WriteUInt32BigEndian(rawMiiBytes, OFS_BROW, browVal);

        // Eye Data (U7) - unknown_5 and unknown_6 are NOT touched
        var eyeVal = BigEndianBinaryHelper.BufferToUint32(rawMiiBytes, OFS_EYE);
        eyeVal =
            (eyeVal & ~MASK_U7_EYE) // Clear U7 bits
            | fragments[fragIndex++]; // Write U7 (5 bits) to LSB bits 0-4
        BigEndianBinaryHelper.WriteUInt32BigEndian(rawMiiBytes, OFS_EYE, eyeVal);

        // Nose Data (U8)
        var noseVal = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_NOSE);
        noseVal = (ushort)(
            (noseVal & ~MASK_U8_NOSE) // Clear U8 bits
            | fragments[fragIndex++]
        ); // Write U8 (3 bits) to LSB bits 0-2
        BigEndianBinaryHelper.WriteUInt16BigEndian(rawMiiBytes, OFS_NOSE, noseVal);

        // Mole Data (U10)
        var moleVal = BigEndianBinaryHelper.BufferToUint16(rawMiiBytes, OFS_MOLE);
        moleVal = (ushort)(
            (moleVal & ~MASK_U10_MOLE) // Clear U10 bit
            | fragments[fragIndex++]
        ); // Write U10 (1 bit) to LSB bit 0
        BigEndianBinaryHelper.WriteUInt16BigEndian(rawMiiBytes, OFS_MOLE, moleVal);
    }

    /// <summary>
    /// Writes a fragment of data (value) of a specific width into a destination uint (dst)
    /// at the current bit position (cursor). Used during the packing process in Extract().
    /// </summary>
    private static void WriteFragment(ref uint dst, ref int cursor, uint value, int width)
    {
        var shiftedValue = value << cursor;
        dst |= shiftedValue;
        cursor += width;
    }

    /// <summary>
    /// Decomposes a packed payload (src) into its constituent fragments
    /// based on the predefined widths in the `_widths` array. Used during the injection process.
    /// </summary>
    private static IEnumerable<uint> Decompose(uint src)
    {
        var cursor = 0;
        foreach (var width in _widths)
        {
            var mask = (1u << width) - 1u;
            yield return (src >> cursor) & mask;
            cursor += width;
        }
    }
}
