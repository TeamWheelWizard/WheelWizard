namespace WheelWizard.MiiRendering.FaceLibrary;

/// <summary>
/// A Mii's looks in the Wii face library's own terms (RFLiCharInfo): part indices, colours and placement as the Wii
/// stores them, which is what the face library's parts are made for.
/// </summary>
public readonly record struct RflCharInfo
{
    public int FaceType { get; init; }
    public int FaceColor { get; init; }
    public int FaceTex { get; init; }
    public int HairType { get; init; }
    public int HairColor { get; init; }
    public bool HairFlip { get; init; }
    public int EyebrowType { get; init; }
    public int EyebrowColor { get; init; }
    public int EyebrowScale { get; init; }
    public int EyebrowRotate { get; init; }
    public int EyebrowX { get; init; }
    public int EyebrowY { get; init; }
    public int EyeType { get; init; }
    public int EyeColor { get; init; }
    public int EyeScale { get; init; }
    public int EyeRotate { get; init; }
    public int EyeX { get; init; }
    public int EyeY { get; init; }
    public int NoseType { get; init; }
    public int NoseScale { get; init; }
    public int NoseY { get; init; }
    public int MouthType { get; init; }
    public int MouthColor { get; init; }
    public int MouthScale { get; init; }
    public int MouthY { get; init; }
    public int MustacheType { get; init; }
    public int BeardType { get; init; }
    public int BeardColor { get; init; }
    public int BeardScale { get; init; }
    public int BeardY { get; init; }
    public int GlassType { get; init; }
    public int GlassColor { get; init; }
    public int GlassScale { get; init; }
    public int GlassY { get; init; }
    public bool HasMole { get; init; }
    public int MoleScale { get; init; }
    public int MoleX { get; init; }
    public int MoleY { get; init; }
    public bool Female { get; init; }
    public int FavoriteColor { get; init; }
    public int Height { get; init; }
    public int Build { get; init; }

    // Studio data splits the Wii's face texture into make-up and wrinkles (see MiiStudioDataSerializer); this undoes it.
    private static readonly (int Makeup, int Wrinkles)[] FaceTexParts =
    [
        (0, 0),
        (1, 0),
        (6, 0),
        (9, 0),
        (0, 5),
        (0, 2),
        (0, 3),
        (0, 7),
        (0, 8),
        (10, 0),
        (0, 9),
        (0, 11),
    ];

    /// <summary>Reads Mii Studio data (hex, as made by MiiStudioDataSerializer) back into the Wii's values.</summary>
    public static OperationResult<RflCharInfo> FromStudioData(string studioHex)
    {
        var decoded = DecodeStudioData(studioHex);
        if (decoded.IsFailure)
            return decoded.Error!;

        var s = decoded.Value;
        var faceTex = Array.IndexOf(FaceTexParts, ((int)s[0x12], (int)s[0x14]));
        return new RflCharInfo
        {
            FaceType = Math.Min((int)s[0x13], 7),
            FaceColor = Math.Min((int)s[0x11], 5),
            FaceTex = Math.Max(faceTex, 0),
            HairType = Math.Min((int)s[0x1D], 71),
            HairColor = HairColorIndex(s[0x1B]),
            HairFlip = s[0x1C] != 0,
            EyebrowType = Math.Min((int)s[0x0E], 23),
            EyebrowColor = HairColorIndex(s[0x0B]),
            EyebrowScale = Math.Min((int)s[0x0D], 8),
            EyebrowRotate = Math.Min((int)s[0x0C], 11),
            EyebrowX = Math.Min((int)s[0x0F], 12),
            EyebrowY = Math.Clamp((int)s[0x10], 3, 18),
            EyeType = Math.Min((int)s[0x07], 47),
            EyeColor = s[0x04] is >= 8 and <= 13 ? s[0x04] - 8 : Math.Min((int)s[0x04], 5),
            EyeScale = Math.Min((int)s[0x06], 7),
            EyeRotate = Math.Min((int)s[0x05], 7),
            EyeX = Math.Min((int)s[0x08], 12),
            EyeY = Math.Min((int)s[0x09], 18),
            NoseType = Math.Min((int)s[0x2C], 11),
            NoseScale = Math.Min((int)s[0x2B], 8),
            NoseY = Math.Min((int)s[0x2D], 18),
            MouthType = Math.Min((int)s[0x26], 23),
            MouthColor = s[0x24] is >= 19 and <= 21 ? s[0x24] - 19 : Math.Min((int)s[0x24], 2),
            MouthScale = Math.Min((int)s[0x25], 8),
            MouthY = Math.Min((int)s[0x27], 18),
            MustacheType = Math.Min((int)s[0x29], 3),
            BeardType = Math.Min((int)s[0x01], 3),
            BeardColor = HairColorIndex(s[0x00]),
            BeardScale = Math.Min((int)s[0x28], 8),
            BeardY = Math.Min((int)s[0x2A], 16),
            GlassType = Math.Min((int)s[0x19], 8),
            GlassColor = s[0x17] switch
            {
                8 => 0,
                >= 14 and <= 18 => s[0x17] - 13,
                _ => Math.Min((int)s[0x17], 5),
            },
            GlassScale = Math.Min((int)s[0x18], 7),
            GlassY = Math.Min((int)s[0x1A], 20),
            HasMole = s[0x20] != 0,
            MoleScale = Math.Min((int)s[0x1F], 8),
            MoleX = Math.Min((int)s[0x21], 16),
            MoleY = Math.Min((int)s[0x22], 30),
            Female = s[0x16] % 2 == 1,
            FavoriteColor = Math.Min((int)s[0x15], 11),
            Height = Math.Min((int)s[0x1E], 127),
            Build = Math.Min((int)s[0x02], 127),
        };
    }

    // Studio data stores the Wii's black (0) as its own black (8).
    private static int HairColorIndex(byte value) =>
        value switch
        {
            8 => 0,
            < 8 => value,
            _ => 0,
        };

    private static OperationResult<byte[]> DecodeStudioData(string studioHex)
    {
        if (string.IsNullOrWhiteSpace(studioHex))
            return Fail("Studio data is empty.");
        if (studioHex.Length != 47 * 2)
            return Fail($"Studio data must decode to 47 bytes but got {studioHex.Length / 2}.");

        byte[] encoded;
        try
        {
            encoded = Convert.FromHexString(studioHex);
        }
        catch (FormatException)
        {
            return Fail("Studio data is not valid hex.");
        }

        // Each byte is obfuscated with the one before it (see MiiStudioDataSerializer.EncodeStudioData).
        var decoded = new byte[46];
        for (var i = 1; i < encoded.Length; i++)
            decoded[i - 1] = (byte)(((encoded[i] - 7 + 256) & 0xFF) ^ encoded[i - 1]);
        return decoded;
    }
}
