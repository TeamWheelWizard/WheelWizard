namespace WheelWizard.WiiManagement.Domain.Mii;

public class MiiFacialHair
{
    public MiiMustacheType MiiMustacheType { get; }
    public MiiBeardType MiiBeardType { get; }
    public MiiHairColor Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    public MiiFacialHair(MiiMustacheType miiMustacheType, MiiBeardType miiBeardType, MiiHairColor color, int size, int vertical)
    {
        if (size is < 0 or > 8)
            throw new ArgumentException("Facial hair size invalid");
        if (vertical is < 0 or > 16)
            throw new ArgumentException("Facial hair vertical position invalid");
        MiiMustacheType = miiMustacheType;
        MiiBeardType = miiBeardType;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiFacialHair> Create(
        MiiMustacheType miiMustacheType,
        MiiBeardType miiBeardType,
        MiiHairColor color,
        int size,
        int vertical
    ) => TryCatch(() => new MiiFacialHair(miiMustacheType, miiBeardType, color, size, vertical));
}
