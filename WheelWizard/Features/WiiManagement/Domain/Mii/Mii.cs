namespace WheelWizard.WiiManagement.Domain.Mii;

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

    public MiiFacialFeatures MiiFacialFeatures { get; set; } =
        new(MiiFaceShape.Bread, MiiSkinColor.Light, MiiFacialFeature.None, false, false);

    public MiiHair MiiHair { get; set; } = new(1, MiiHairColor.Black, false);
    public MiiEyebrow MiiEyebrows { get; set; } = new(1, 0, MiiHairColor.Black, 4, 10, 1);
    public MiiEye MiiEyes { get; set; } = new(1, 6, 7, MiiEyeColor.Black, 3, 6);
    public MiiNose MiiNose { get; set; } = new(MiiNoseType.Default, 6, 4);
    public MiiLip MiiLips { get; set; } = new(1, MiiLipColor.Skin, 4, 9);
    public MiiGlasses MiiGlasses { get; set; } = new(MiiGlassesType.None, MiiGlassesColor.Grey, 4, 1);
    public MiiFacialHair MiiFacialHair { get; set; } = new(MiiMustacheType.None, MiiBeardType.None, MiiHairColor.Black, 1, 1);
    public MiiMole MiiMole { get; set; } = new(false, 0, 0, 0);
    public MiiName CreatorName { get; set; } = new("no name");
}
