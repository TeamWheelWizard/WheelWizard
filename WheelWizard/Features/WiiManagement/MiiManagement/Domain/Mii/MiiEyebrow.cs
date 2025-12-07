namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

public class MiiEyebrow
{
    public int Type { get; }
    public int Rotation { get; }
    public MiiHairColor Color { get; }
    public int Size { get; }
    public int Vertical { get; }
    public int Spacing { get; }

    public MiiEyebrow(int type, int rotation, MiiHairColor color, int size, int vertical, int spacing)
    {
        if (type is < 0 or > 23)
            throw new ArgumentException("Eyebrow type invalid");
        if (rotation is < 0 or > 11)
            throw new ArgumentException("Rotation invalid");
        if (size is < 0 or > 8)
            throw new ArgumentException("Size invalid");
        if (vertical is < 3 or > 18)
            throw new ArgumentException("Vertical position invalid");
        if (spacing is < 0 or > 12)
            throw new ArgumentException("Spacing invalid");
        Type = type;
        Rotation = rotation;
        Color = color;
        Size = size;
        Vertical = vertical;
        Spacing = spacing;
    }

    public static OperationResult<MiiEyebrow> Create(int type, int rotation, MiiHairColor color, int size, int vertical, int spacing) =>
        TryCatch(() => new MiiEyebrow(type, rotation, color, size, vertical, spacing));
}
