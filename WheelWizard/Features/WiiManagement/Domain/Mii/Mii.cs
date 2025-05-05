using WheelWizard.WiiManagement.Domain.Mii.Custom;

namespace WheelWizard.WiiManagement.Domain.Mii;

/*
Mii data Structure  from the wii looks like this:
0x00-0x01 :
  - Bit 0    : invalid
  - Bit 1    : isGirl
  - Bits 2–5 : month (1–12)
  - Bits 6–10: day (1–31)
  - Bits 11–14: favColor (0–11+)
  - Bit 15   : isFavorite

0x02–0x15 : name (10 UTF-16 characters, 20 bytes total)
0x16 : height (0–127)
0x17 : weight (0–127)

(timestamp-based unique ID) includes "special" flags in the top 3 bits (maybe top 4 which would make the entire MiiID1 responsible for the Mii type)
0x18 : miiID1 (part 1)
0x19 : miiID2 (part 2)
0x1A : miiID3 (part 3; top 3 bits = special/foreign/regular)
0x1B : miiID4 (part 4)

0x1C : systemID0 (MAC hash checksum byte)
0x1D : systemID1 (MAC byte 4)
0x1E : systemID2 (MAC byte 5)
0x1F : systemID3 (MAC byte 6)

0x20–0x21 :
  - Bits 0–2   : faceShape
  - Bits 3–5   : skinColor
  - Bits 6–9   : facialFeature
  - Bits 10–12 : unknown_0 (3 bits long)
  - Bit 13     : mingleOff (1 = don't mingle)
  - Bit 14     : unknown_1 (1 bit long)
  - Bit 15     : downloaded (1 = downloaded Mii)

0x22–0x23 :
  - Bits 0–6   : hairType (0–71)
  - Bits 7–9   : hairColor (0–7)
  - Bit 10     : hairPart (0 = normal, 1 = mirrored)
  - Bits 11–15 : unknown_2 (5 bits long)

0x24–0x27 :
  - Bits 0–4   : eyebrowType (0–23)
  - Bit 5      : unknown_3 (1 bit long)
  - Bits 6–9   : eyebrowRotation (0–11)
  - Bits 10–15 : unknown_4 (6 bits long)
  - Bits 16–18 : eyebrowColor (0–7)
  - Bits 19–22 : eyebrowSize (0–8)
  - Bits 23–27 : eyebrowVertPos (3–18)
  - Bits 28–31 : eyebrowHorizSpacing (0–12)

0x28–0x2B :
  - Bits 0–5   : eyeType (0–47)
  - Bits 6–7   : unknown_5 (2 bits long)
  - Bits 8–10  : eyeRotation (0–7)
  - Bits 11–15 : eyeVertPos (0–18)
  - Bits 16–18 : eyeColor (0–5)
  - Bit 19     : unknown_6 (1 bit long)
  - Bits 20–22 : eyeSize (0–7)
  - Bits 23–26 : eyeHorizSpacing (0–12)
  - Bits 27–31 : unknown_7 (5 bits long)

0x2C–0x2D :
  - Bits 0–3   : noseType (0–11)
  - Bits 4–7   : noseSize (0–8)
  - Bits 8–12  : noseVertPos (0–18)
  - Bits 13–15 : unknown_8 (3 bits long)

0x2E–0x2F :
  - Bits 0–4   : lipType (0–23)
  - Bits 5–6   : lipColor (0–2)
  - Bits 7–10  : lipSize (0–8)
  - Bits 11–15 : lipVertPos (0–18)

0x30–0x31 :
  - Bits 0–3   : glassesType (0–8)
  - Bits 4–6   : glassesColor (0–5)
  - Bit 7      : unknown_9 (if set, Mii may not render) so not counted in our total free bits
  - Bits 8–10  : glassesSize (0–7)
  - Bits 11–15 : glassesVertPos (0–20)

0x32–0x33 :
  - Bits 0–1   : mustacheType (0–3)
  - Bits 2–3   : beardType (0–3)
  - Bits 4–6   : facialHairColor (0–7)
  - Bits 7–10  : mustacheSize (0–8)
  - Bits 11–15 : mustacheVertPos (0–16)

0x34–0x35 :
  - Bit 0      : moleOn (1 = has mole)
  - Bits 1–4   : moleSize (0–8)
  - Bits 5–9   : moleVertPos (0–30)
  - Bits 10–14 : moleHorizPos (0–16)
  - Bit 15     : unknown_10 (1 bit long)

0x36–0x49 : creatorName (10 UTF-16 characters, 20 bytes total)


We can use these Unknown bits to store extra data, Like the ability to say if you want your mii to be copiable or not

Free bits available for custom use and what we will map them to:
This gives us a total of 28 bits to play with.
----------------------------------------------------
0x20–0x21 : Bits 10–12   (unknown_0, 3 bits)
0x20–0x21 : Bit 14       (unknown_1, 1 bit) BREAKS IT
0x22–0x23 : Bits 11–15   (unknown_2, 5 bits) top 2 bits break it
0x24–0x27 : Bit 5        (unknown_3, 1 bit)
0x24–0x27 : Bits 10–15   (unknown_4, 6 bits) bit 3 breaks it
0x28–0x2B : Bits 6–7     (unknown_5, 2 bits)
0x28–0x2B : Bit 19       (unknown_6, 1 bit)
0x28–0x2B : Bits 27–31   (unknown_7, 5 bits)
0x2C–0x2D : Bits 13–15   (unknown_8, 3 bits)
0x34–0x35 : Bit 15       (unknown_10, 1 bit)
*/

public class Mii
{
    public bool IsInvalid { get; set; }
    public bool IsGirl { get; set; }
    public DateOnly Date { get; set; } = new(2000, 1, 1);
    public MiiFavoriteColor MiiFavoriteColor { get; set; } = MiiFavoriteColor.Black;
    public bool IsFavorite { get; set; }

    public MiiName Name { get; set; } = new("no name");
    public MiiScale Height { get; set; } = new(1);
    public MiiScale Weight { get; set; } = new(1);

    public bool IsForeign => (MiiId1 >> 5) == 0b110; // checks if the top 3 bits are set to 110, if so it got blue pants

    //Mii ID is also refered as Avatar  ID
    public byte MiiId1 { get; set; }
    public byte MiiId2 { get; set; }
    public byte MiiId3 { get; set; }
    public byte MiiId4 { get; set; }

    public uint MiiId
    {
        get => (uint)(MiiId1 << 24 | MiiId2 << 16 | MiiId3 << 8 | MiiId4);
        set
        {
            MiiId1 = (byte)(value >> 24);
            MiiId2 = (byte)(value >> 16);
            MiiId3 = (byte)(value >> 8);
            MiiId4 = (byte)(value);
        }
    }

    //This is also referred as Client ID
    public byte SystemId0 { get; set; }
    public byte SystemId1 { get; set; }
    public byte SystemId2 { get; set; }
    public byte SystemId3 { get; set; }

    public uint SystemId
    {
        get => (uint)(SystemId0 << 24 | SystemId1 << 16 | SystemId2 << 8 | SystemId3);
        set
        {
            SystemId0 = (byte)(value >> 24);
            SystemId1 = (byte)(value >> 16);
            SystemId2 = (byte)(value >> 8);
            SystemId3 = (byte)(value);
        }
    }

    /// <summary>
    /// The 28 unknown Wii‑Mii bits, exposed through a high‑level wrapper.
    /// Always initialised (never null).
    /// </summary>
    public CustomMiiData CustomData { get; set; } = CustomMiiData.CreateEmpty();
    public MiiFacialFeatures MiiFacial { get; set; } = new(MiiFaceShape.Bread, MiiSkinColor.Light, MiiFacialFeature.None, false, false);

    public MiiHair MiiHair { get; set; } = new(1, HairColor.Black, false);
    public MiiEyebrow MiiEyebrows { get; set; } = new(1, 0, EyebrowColor.Black, 4, 10, 1);
    public MiiEye MiiEyes { get; set; } = new(1, 6, 7, EyeColor.Black, 3, 6);
    public MiiNose MiiNose { get; set; } = new(NoseType.Default, 6, 4);
    public MiiLip MiiLips { get; set; } = new(1, LipColor.Skin, 4, 9);
    public MiiGlasses MiiGlasses { get; set; } = new(GlassesType.None, GlassesColor.Dark, 4, 1);
    public MiiFacialHair MiiFacialHair { get; set; } = new(MustacheType.None, BeardType.None, MustacheColor.Black, 1, 1);
    public MiiMole MiiMole { get; set; } = new(false, 0, 0, 0);
    public MiiName CreatorName { get; set; } = new("no name");
}
