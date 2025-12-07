using System.Text;
using WheelWizard.Helpers;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages;

public class MiiStudioDataSerializer
{
    private static readonly int[] MakeupMap = [0, 1, 6, 9, 0, 0, 0, 0, 0, 10, 0, 0];
    private static readonly int[] WrinklesMap = [0, 0, 0, 0, 5, 2, 3, 7, 8, 0, 9, 11];

    /// <summary>
    /// Serialize the Mii in t the encoded data string required by the Nintendo Mii Image URL.
    /// </summary>
    public static OperationResult<string> Serialize(Mii? mii)
    {
        if (mii == null)
            return Fail("Mii cannot be null.");

        // First we create a clone of the Mii that only contains features that are visual
        // This means no name, date or other non-visual features.
        // That way when you change a feature that is not visual, it will not result in different bytes.
        Mii visualMiiClone = new();
        visualMiiClone.MiiEyebrows = mii.MiiEyebrows;
        visualMiiClone.MiiEyes = mii.MiiEyes;
        visualMiiClone.MiiFacialHair = mii.MiiFacialHair;
        visualMiiClone.MiiGlasses = mii.MiiGlasses;
        visualMiiClone.MiiHair = mii.MiiHair;
        visualMiiClone.MiiLips = mii.MiiLips;
        visualMiiClone.MiiNose = mii.MiiNose;
        visualMiiClone.Height = mii.Height;
        visualMiiClone.Weight = mii.Weight;
        visualMiiClone.IsGirl = mii.IsGirl;
        visualMiiClone.MiiMole = mii.MiiMole;
        visualMiiClone.MiiFavoriteColor = mii.MiiFavoriteColor;
        visualMiiClone.MiiFacialFeatures = mii.MiiFacialFeatures;
        // If id is 0, we keep it 0, any other ID will be set to 1.
        visualMiiClone.MiiId = (uint)(mii.MiiId == 0 ? 0 : 1);

        var serialized = MiiSerializer.Serialize(visualMiiClone);
        if (serialized.IsFailure)
            return serialized.Error;

        var bytes = GenerateStudioDataArray(serialized.Value);
        return Ok(EncodeStudioData(bytes));
    }

    /// <summary>
    /// Encodes the studio data array into the hex string format required by the API.
    /// Based on the encodeStudio function in the provided JS.
    /// </summary>
    private static string EncodeStudioData(byte[] studioData)
    {
        byte n = 0;
        var dest = new StringBuilder("00", (studioData.Length + 1) * 2); // Preallocate buffer ("00" + 2 chars per byte)

        foreach (var b in studioData)
        {
            var eo = (byte)((7 + (b ^ n)) & 0xFF);
            n = eo; // Update n *after* calculating eo, using the new eo
            dest.Append(eo.ToString("x2")); // Append hex representation
        }
        return dest.ToString();
    }

    /// <summary>
    /// Parses the Wii Mii data and generates the 46-byte studio data array.
    /// Based on the miiFileRead logic for Wii data in the provided JS.
    /// Uses BigEndianBinaryReader for reading values.
    /// </summary>
    private static byte[] GenerateStudioDataArray(byte[] buf)
    {
        var studio = new byte[46]; // Size of studio data array

        // Parse Wii data fields and map them to studio array indices
        // Offsets and logic match the 'else' block (Wii part) of miiFileRead

        // --- Basic Info ---
        var tmpU16_0 = BigEndianBinaryHelper.BufferToUint16(buf, 0);
        var isGirl = ((tmpU16_0 >> 14) & 1) == 1;
        var favColor = (int)((tmpU16_0 >> 1) & 0xF);
        int height = buf[0x16];
        int weight = buf[0x17];

        studio[0x16] = (byte)(isGirl ? 1 : 0); // Gender
        studio[0x15] = (byte)favColor; // Favorite Color
        studio[0x1E] = (byte)height; // Height
        studio[2] = (byte)weight; // Weight (mapped to index 2 in studio)

        // --- Face ---
        var tmpU16_20 = BigEndianBinaryHelper.BufferToUint16(buf, 0x20);
        var faceShape = (int)(tmpU16_20 >> 13);
        var skinColor = (int)((tmpU16_20 >> 10) & 7);
        var facialFeature = (int)((tmpU16_20 >> 6) & 0xF); // Note: JS uses 0xF mask here, map to makeup/wrinkles
        var makeup = MakeupMap.Length > facialFeature ? MakeupMap[facialFeature] : 0;
        var wrinkles = WrinklesMap.Length > facialFeature ? WrinklesMap[facialFeature] : 0;

        studio[0x13] = (byte)faceShape;
        studio[0x11] = (byte)skinColor;
        studio[0x14] = (byte)wrinkles;
        studio[0x12] = (byte)makeup;

        // --- Hair ---
        var tmpU16_22 = BigEndianBinaryHelper.BufferToUint16(buf, 0x22);
        var hairStyle = (int)(tmpU16_22 >> 9);
        var hairColor = (int)((tmpU16_22 >> 6) & 7);
        var flipHair = (int)((tmpU16_22 >> 5) & 1);

        studio[0x1D] = (byte)hairStyle;
        studio[0x1B] = (byte)(hairColor == 0 ? 8 : hairColor); // Map color 0 to 8
        studio[0x1C] = (byte)flipHair;

        // --- Eyebrows ---
        var tmpU32_24 = BigEndianBinaryHelper.BufferToUint32(buf, 0x24);
        var eyebrowStyle = (int)(tmpU32_24 >> 27);
        var eyebrowRotation = (int)((tmpU32_24 >> 22) & 0xF); // Note: JS uses 0xF mask
        var eyebrowColor = (int)((tmpU32_24 >> 13) & 7);
        var eyebrowScale = (int)((tmpU32_24 >> 9) & 0xF);
        var eyebrowYScale = 3; // Hardcoded in JS
        var eyebrowYPosition = (int)((tmpU32_24 >> 4) & 0x1F);
        var eyebrowXSpacing = (int)(tmpU32_24 & 0xF);

        studio[0xE] = (byte)eyebrowStyle;
        studio[0xC] = (byte)eyebrowRotation;
        studio[0xB] = (byte)(eyebrowColor == 0 ? 8 : eyebrowColor); // Map color 0 to 8
        studio[0xD] = (byte)eyebrowScale;
        studio[0xA] = (byte)eyebrowYScale;
        studio[0x10] = (byte)eyebrowYPosition;
        studio[0xF] = (byte)eyebrowXSpacing;

        // --- Eyes ---
        var tmpU32_28 = BigEndianBinaryHelper.BufferToUint32(buf, 0x28);
        var eyeStyle = (int)(tmpU32_28 >> 26);
        var eyeRotation = (int)((tmpU32_28 >> 21) & 7); // Note: JS uses 7 (0b111) mask
        var eyeYPosition = (int)((tmpU32_28 >> 16) & 0x1F);
        var eyeColor = (int)((tmpU32_28 >> 13) & 7);
        var eyeScale = (int)((tmpU32_28 >> 9) & 7); // Note: JS uses 7 mask
        var eyeYScale = 3; // Hardcoded in JS
        var eyeXSpacing = (int)((tmpU32_28 >> 5) & 0xF);
        // int unknownEyeBit = (int)(tmpU32_28 & 0x1F); // Lower 5 bits unused in JS mapping

        studio[7] = (byte)eyeStyle;
        studio[5] = (byte)eyeRotation;
        studio[9] = (byte)eyeYPosition;
        studio[4] = (byte)(eyeColor + 8); // Map color 0-7 to 8-15
        studio[6] = (byte)eyeScale;
        studio[3] = (byte)eyeYScale;
        studio[8] = (byte)eyeXSpacing;

        // --- Nose ---
        var tmpU16_2C = BigEndianBinaryHelper.BufferToUint16(buf, 0x2C);
        var noseStyle = (int)(tmpU16_2C >> 12);
        var noseScale = (int)((tmpU16_2C >> 8) & 0xF);
        var noseYposition = (int)((tmpU16_2C >> 3) & 0x1F);
        // int unknownNoseBits = (int)(tmpU16_2C & 7); // Lower 3 bits unused

        studio[0x2C] = (byte)noseStyle;
        studio[0x2B] = (byte)noseScale;
        studio[0x2D] = (byte)noseYposition;

        // --- Mouth ---
        var tmpU16_2E = BigEndianBinaryHelper.BufferToUint16(buf, 0x2E);
        var mouseStyle = (int)(tmpU16_2E >> 11);
        var mouseColor = (int)((tmpU16_2E >> 9) & 3); // Lip color (0-3)
        var mouseScale = (int)((tmpU16_2E >> 5) & 0xF);
        var mouseYScale = 3; // Hardcoded in JS
        var mouseYPosition = (int)(tmpU16_2E & 0x1F);

        studio[0x26] = (byte)mouseStyle;
        studio[0x24] = (byte)(mouseColor < 4 ? mouseColor + 19 : 0); // Map 0-3 to 19-22, else 0
        studio[0x25] = (byte)mouseScale;
        studio[0x23] = (byte)mouseYScale;
        studio[0x27] = (byte)mouseYPosition;

        // --- Beard / Mustache ---
        var tmpU16_32 = BigEndianBinaryHelper.BufferToUint16(buf, 0x32);
        var mustacheStyle = (int)(tmpU16_32 >> 14);
        var beardStyle = (int)((tmpU16_32 >> 12) & 3);
        var facialHairColor = (int)((tmpU16_32 >> 9) & 7);
        var mustacheScale = (int)((tmpU16_32 >> 5) & 0xF);
        var mustacheYPosition = (int)(tmpU16_32 & 0x1F);

        studio[0x29] = (byte)mustacheStyle;
        studio[1] = (byte)beardStyle; // Mapped to index 1
        studio[0] = (byte)(facialHairColor == 0 ? 8 : facialHairColor); // Map color 0 to 8, Mapped to index 0
        studio[0x28] = (byte)mustacheScale;
        studio[0x2A] = (byte)mustacheYPosition;

        // --- Glasses ---
        var tmpU16_30 = BigEndianBinaryHelper.BufferToUint16(buf, 0x30);
        var glassesStyle = (int)(tmpU16_30 >> 12);
        var glassesColor = (int)((tmpU16_30 >> 9) & 7);
        var glassesScale = (int)((tmpU16_30 >> 5) & 7); // Note: JS uses 7 mask
        var glassesYPosition = (int)(tmpU16_30 & 0x1F);
        // int unknownGlassesBits = (int)((tmpU16_30 >> 12) & 7); // Middle bits unused

        studio[0x19] = (byte)glassesStyle;
        byte mappedGlassesColor;
        if (glassesColor == 0)
            mappedGlassesColor = 8; // black -> 8
        else if (glassesColor < 6)
            mappedGlassesColor = (byte)(glassesColor + 13); // 1-5 -> 14-18
        else
            mappedGlassesColor = 0; // 6, 7 -> 0 (no mapping?)
        studio[0x17] = mappedGlassesColor;
        studio[0x18] = (byte)glassesScale;
        studio[0x1A] = (byte)glassesYPosition;

        // --- Mole ---
        var tmpU16_34 = BigEndianBinaryHelper.BufferToUint16(buf, 0x34);
        var enableMole = (int)(tmpU16_34 >> 15);
        var moleScale = (int)((tmpU16_34 >> 11) & 0xF);
        var moleYPosition = (int)((tmpU16_34 >> 6) & 0x1F);
        var moleXPosition = (int)((tmpU16_34 >> 1) & 0x1F);
        // int unknownMoleBit = (int)(tmpU16_34 & 1); // Lowest bit unused

        studio[0x20] = (byte)enableMole;
        studio[0x1F] = (byte)moleScale;
        studio[0x22] = (byte)moleYPosition;
        studio[0x21] = (byte)moleXPosition;

        return studio;
    }
}
