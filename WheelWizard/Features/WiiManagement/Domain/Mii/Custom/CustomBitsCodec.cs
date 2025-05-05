using WheelWizard.Services.WiiManagement.SaveData;

/// <summary>
/// Handles the low-level reading and writing of the 28 custom bits spread across
/// different locations within the 74-byte raw Mii data format.
/// It ensures that only the designated "unknown" bits are touched, preserving
/// the official Mii data structure.
///
/// Extraction (`Extract`): Reads the relevant sections of the Mii data, isolates the specific
///                         unused bits, and packs them together into a single 28-bit `uint`.
/// Injection (`Inject`): Takes a 28-bit `uint` payload, unpacks it into fragments, reads the
///                       relevant Mii data sections, clears only the unused bits, and writes
///                       the fragments back into their original locations.
/// </summary>
internal static class CustomBitsCodec
{
    // Define constants for the byte offsets within the 74-byte Mii array where
    // the known unused bits reside. Refer to Mii.cs comments for the structure.

    // Location: Face data (0x20-0x21, 2 bytes)
    // Contains: unknown_0 (3 bits at original bits 10-12) & unknown_1 (1 bit at original bit 14)
    // Note: Bit numbering in Mii spec is often from MSB (bit 15) to LSB (bit 0). Here, we map
    // to standard 0-indexed LSB representation for masks.
    // E.g., Mii spec bits 10-12 correspond to bits 3-5 in a standard ushort (0-15).
    // E.g., Mii spec bit 14 corresponds to bit 1 in a standard ushort.
    private const int OFS_FACE = 0x20;

    // Mask for unknown_0 (bits 3, 4, 5 of the ushort at OFS_FACE): 0b0000 0000 0011 1000 = 0x0038
    // Mask for unknown_1 (bit 1 of the ushort at OFS_FACE):       0b0000 0000 0000 0010 = 0x0002


    // I wont explain this for EVERY field but this is the general idea:
    // so we have the address 0x20 and in the documentation you can see that 16 bits are used

    // u16 faceShape:3; // 0 - 7
    // u16 skinColor:3; // 0 - 5
    // u16 facialFeature:4; // 0 - 11
    // u16 unknown:3; // Mii appears unaffected by changes to this data
    // u16 mingleOff:1; // 0 = Mingle, 1 = Don't Mingle
    // u16 unknown:1; // Mii appears unaffected by changes to this data
    // u16 downloaded:1; // If the Mii has been downloaded from the Check Mii Out Channel

    // If we map this out we have these 16 bits 0000 0000 0000 0000
    // Those numbers are basically the position of the bits in that 16 bit number
    // so the Last bit is the "downloaded" bit, 0000 0000 0000 0001 (This is downloaded set to 1)
    // the bitmask is basically a mask saying, "hey, the bits set to 1 is the bit that is the relevent bit for this information"
    // so the bitmask for the "Unknown:1" is 0000 0000 0000 0010 because it is the second last bit


    // Location: Hair data (0x22-0x23, 2 bytes)
    // Contains: unknown_2 (5 bits at original bits 11-15)
    // Mapped to bits 0-4 of the ushort at OFS_HAIR.
    private const int OFS_HAIR = 0x22;

    // Mask for unknown_2 (bits 0, 1, 2, 3, 4): 0b0000 0000 0001 1111 = 0x001F
    private const ushort MASK_U2 = 0x001F; // Direct mask for bits 0-4

    // Location: Eyebrow data (0x24-0x27, 4 bytes)
    // Contains: unknown_3 (1 bit at original bit 5) & unknown_4 (6 bits at original bits 10-15)
    // Mapped to bit 26 (unknown_3) and bits 16-21 (unknown_4) of the uint at OFS_BROW.
    private const int OFS_BROW = 0x24;

    // Mask for unknown_3 (bit 26): 1 shifted left 26 times.
    private const uint MASK_U3 = 1u << 26; // 0b0000 0100 0000 0000 0000 0000 0000 0000 = 0x04000000

    // Mask for unknown_4 (bits 16-21): 6 ones shifted left 16 times. (0b111111 << 16)
    private const uint MASK_U4 = 0x003F0000; // 0b0000 0000 0011 1111 0000 0000 0000 0000

    // Location: Eye data (0x28-0x2B, 4 bytes)
    // Contains: unknown_5 (2 bits at original bits 6-7), unknown_6 (1 bit at original bit 19), unknown_7 (5 bits at original bits 27-31)
    // Mapped to bits 24-25 (unknown_5), bit 12 (unknown_6), and bits 0-4 (unknown_7) of the uint at OFS_EYE.
    private const int OFS_EYE = 0x28;

    // Mask for unknown_5 (bits 24-25): 0b11 shifted left 24 times.
    private const uint MASK_U5 = 0x03000000; // 0b0000 0011 0000 0000 0000 0000 0000 0000

    // Mask for unknown_6 (bit 12): 1 shifted left 12 times.
    // too lazy to explain but since its big endian we have to shift it 12 times
    private const uint MASK_U6 = 1u << 12; // 0b0000 0000 0000 0001 0000 0000 0000 0000 = 0x00001000

    // Mask for unknown_7 (bits 0-4): 5 ones.
    private const uint MASK_U7 = 0x0000001F; // 0b0000 0000 0000 0000 0000 0000 0001 1111

    // Location: Nose data (0x2C-0x2D, 2 bytes)
    // Contains: unknown_8 (3 bits at original bits 13-15)
    // Mapped to bits 0-2 of the ushort at OFS_NOSE.
    private const int OFS_NOSE = 0x2C;

    // Mask for unknown_8 (bits 0-2): 3 ones.
    private const ushort MASK_U8 = 0x0007; // 0b0000 0000 0000 0111

    // Location: Mole data (0x34-0x35, 2 bytes)
    // Contains: unknown_10 (1 bit at original bit 15)
    // Mapped to bit 0 of the ushort at OFS_MOLE.
    private const int OFS_MOLE = 0x34;

    // Mask for unknown_10 (bit 0): 1.
    private const ushort MASK_U10 = 0x0001; // 0b0000 0000 0000 0001

    // Defines the width (in bits) of each unknown fragment, in the order they are packed.
    // This MUST match the order of operations in Extract() and Inject().
    // Total width must sum to TOTAL_BITS (28).
    // Order: U0, U1, U2, U3, U4, U5, U6, U7, U8, U10
    private static readonly int[] _widths = { 3, 1, 5, 1, 6, 2, 1, 5, 3, 1 }; // Sum = 28

    /// <summary>
    /// Extracts the 28 designated unused bits from the raw Mii data and packs them
    /// into a single right-aligned 28-bit unsigned integer.
    /// </summary>
    /// <param name="rawMiiBytes">The 74-byte array containing Mii data.</param>
    internal static uint Extract(byte[] rawMiiBytes)
    {
        // Read the relevant multibyte chunks from the raw data array.
        var faceData = BigEndianBinaryReader.ReadUint16(rawMiiBytes, OFS_FACE); // Read bytes 0x20, 0x21
        var hairData = BigEndianBinaryReader.ReadUint16(rawMiiBytes, OFS_HAIR); // Read bytes 0x22, 0x23
        var browData = BigEndianBinaryReader.ReadUint32(rawMiiBytes, OFS_BROW); // Read bytes 0x24-0x27
        var eyeData = BigEndianBinaryReader.ReadUint32(rawMiiBytes, OFS_EYE); // Read bytes 0x28-0x2B
        var noseData = BigEndianBinaryReader.ReadUint16(rawMiiBytes, OFS_NOSE); // Read bytes 0x2C, 0x2D
        var moleData = BigEndianBinaryReader.ReadUint16(rawMiiBytes, OFS_MOLE); // Read bytes 0x34, 0x35

        // Initialize the payload to store the extracted bits and a cursor for packing.
        uint payload = 0;
        var cursor = 0; // Tracks the next bit position to write into the payload (starts at LSB).

        // Extract and pack each fragment in the predefined order (_widths array).
        // Fragment 1: unknown_0 (3 bits from faceData, bits 3-5)
        var frag_u0 = (uint)((faceData & 0x0038) >> 3); // Isolate bits 3-5 (0x38 mask) and shift right by 3.
        WriteFragment(ref payload, ref cursor, frag_u0, 3); // Write 3 bits to payload.

        // Fragment 2: unknown_1 (1 bit from faceData, bit 1)
        var frag_u1 = (uint)((faceData & 0x0002) >> 1); // Isolate bit 1 (0x02 mask) and shift right by 1.
        WriteFragment(ref payload, ref cursor, frag_u1, 1); // Write 1 bit.

        // Fragment 3: unknown_2 (5 bits from hairData, bits 0-4)
        var frag_u2 = (uint)(hairData & MASK_U2); // Isolate bits 0-4 (MASK_U2). No shift needed.
        WriteFragment(ref payload, ref cursor, frag_u2, 5); // Write 5 bits.

        // Fragment 4: unknown_3 (1 bit from browData, bit 26)
        var frag_u3 = (browData & MASK_U3) >> 26; // Isolate bit 26 (MASK_U3) and shift right by 26.
        WriteFragment(ref payload, ref cursor, frag_u3, 1); // Write 1 bit.

        // Fragment 5: unknown_4 (6 bits from browData, bits 16-21)
        var frag_u4 = (browData & MASK_U4) >> 16; // Isolate bits 16-21 (MASK_U4) and shift right by 16.
        WriteFragment(ref payload, ref cursor, frag_u4, 6); // Write 6 bits.

        // Fragment 6: unknown_5 (2 bits from eyeData, bits 24-25)
        var frag_u5 = (eyeData & MASK_U5) >> 24; // Isolate bits 24-25 (MASK_U5) and shift right by 24.
        WriteFragment(ref payload, ref cursor, frag_u5, 2); // Write 2 bits.

        // Fragment 7: unknown_6 (1 bit from eyeData, bit 12)
        var frag_u6 = (eyeData & MASK_U6) >> 12; // Isolate bit 12 (MASK_U6) and shift right by 12.
        WriteFragment(ref payload, ref cursor, frag_u6, 1); // Write 1 bit.

        // Fragment 8: unknown_7 (5 bits from eyeData, bits 0-4)
        var frag_u7 = eyeData & MASK_U7; // Isolate bits 0-4 (MASK_U7). No shift needed.
        WriteFragment(ref payload, ref cursor, frag_u7, 5); // Write 5 bits.

        // Fragment 9: unknown_8 (3 bits from noseData, bits 0-2)
        var frag_u8 = (uint)(noseData & MASK_U8); // Isolate bits 0-2 (MASK_U8). No shift needed.
        WriteFragment(ref payload, ref cursor, frag_u8, 3); // Write 3 bits.

        // Fragment 10: unknown_10 (1 bit from moleData, bit 0)
        var frag_u10 = (uint)(moleData & MASK_U10); // Isolate bit 0 (MASK_U10). No shift needed.
        WriteFragment(ref payload, ref cursor, frag_u10, 1); // Write 1 bit.

        // The payload now contains all 28 custom bits packed sequentially.
        return payload;
    }

    /// <summary>
    /// Injects a 28-bit custom data payload back into the designated unused bit locations
    /// within a raw 74-byte Mii data array. Modifies the array in place.
    /// </summary>
    /// <param name="raw">The 74-byte array representing Mii data. This array will be modified.</param>
    /// <param name="payload">The 28-bit custom data payload (stored in the lower bits of the uint).</param>
    /// <summary>Injects <paramref name="payload"/> back into <paramref name="raw"/>, preserving all official bits.</summary>
    internal static void Inject(byte[] raw, uint payload)
    {
        // Decompose the payload in the same order we packed it
        var fragments = Decompose(payload).ToArray();
        var i = 0;

        var face = BigEndianBinaryReader.ReadUint16(raw, OFS_FACE);
        face = (ushort)(
            (uint)(face & ~0x003A) // wipe U0+U1 (0x38 | 0x02)
            | (fragments[i++] << 3) // U0 (3b) → bits 3‑5
            | (fragments[i++] << 1)
        ); // U1 (1b) → bit 1
        BigEndianBinaryReader.WriteUInt16(raw, OFS_FACE, face);

        var hair = BigEndianBinaryReader.ReadUint16(raw, OFS_HAIR);
        hair = (ushort)((hair & ~MASK_U2) | fragments[i++]); // U2 (5b)
        BigEndianBinaryReader.WriteUInt16(raw, OFS_HAIR, hair);

        var brow = BigEndianBinaryReader.ReadUint32(raw, OFS_BROW);
        brow =
            (brow & ~(MASK_U3 | MASK_U4))
            | (fragments[i++] << 26) // U3 (1b)
            | (fragments[i++] << 16); // U4 (6b)
        BigEndianBinaryReader.WriteUInt32(raw, OFS_BROW, brow);

        var eye = BigEndianBinaryReader.ReadUint32(raw, OFS_EYE);
        eye =
            (eye & ~(MASK_U5 | MASK_U6 | MASK_U7))
            | (fragments[i++] << 24) // U5 (2b)
            | (fragments[i++] << 12) // U6 (1b)
            | fragments[i++]; // U7 (5b)
        BigEndianBinaryReader.WriteUInt32(raw, OFS_EYE, eye);

        var nose = BigEndianBinaryReader.ReadUint16(raw, OFS_NOSE);
        nose = (ushort)((nose & ~MASK_U8) | fragments[i++]); // U8 (3b)
        BigEndianBinaryReader.WriteUInt16(raw, OFS_NOSE, nose);

        var mole = BigEndianBinaryReader.ReadUint16(raw, OFS_MOLE);
        mole = (ushort)((mole & ~MASK_U10) | fragments[i]); // U10 (1b)
        BigEndianBinaryReader.WriteUInt16(raw, OFS_MOLE, mole);
    }

    /// <summary>
    /// Writes a fragment of data (value) of a specific width into a destination uint (dst)
    /// at the current bit position (cursor). Used during the packing process in Extract().
    /// </summary>
    /// <param name="dst">The destination uint payload (modified by reference).</param>
    /// <param name="cursor">The current bit position to start writing at (modified by reference).</param>
    /// <param name="value">The data fragment to write (should fit within 'width' bits).</param>
    /// <param name="width">The number of bits this fragment occupies.</param>
    private static void WriteFragment(ref uint dst, ref int cursor, uint value, int width)
    {
        // Shift the value left by the cursor amount to position it correctly.
        var shiftedValue = value << cursor;
        // Use bitwise OR to merge the shifted value into the destination payload.
        dst |= shiftedValue;
        // Advance the cursor by the width of the fragment just written.
        cursor += width;
    }

    /// <summary>
    /// Decomposes a packed 28-bit payload (src) into its constituent fragments
    /// based on the predefined widths in the `_widths` array. Used during the injection process.
    /// </summary>
    /// <param name="src">The packed 28-bit payload.</param>
    /// <returns>An enumerable sequence of uint fragments in the order they were packed.</returns>
    private static IEnumerable<uint> Decompose(uint src)
    {
        var cursor = 0; // Tracks the current bit position being read from the source payload.
        // Iterate through the known widths of the fragments in their packing order.
        foreach (var width in _widths)
        {
            // Create a mask for the current fragment width (e.g., width 3 -> mask 0b111).
            var mask = (1u << width) - 1u;
            // Extract the fragment:
            // 1. Right-shift the source payload by the cursor position to bring the fragment to the LSB.
            // 2. Apply the mask to isolate only the bits of the current fragment.
            yield return (src >> cursor) & mask;
            // Advance the cursor for the next fragment.
            cursor += width;
        }
    }
}
