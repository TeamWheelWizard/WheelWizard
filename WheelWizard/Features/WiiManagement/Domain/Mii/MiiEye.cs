namespace WheelWizard.WiiManagement.Domain.Mii;

public class MiiEye
{
    public int Type { get; }
    public int Rotation { get; }
    public int Vertical { get; }
    public MiiEyeColor Color { get; }
    public int Size { get; }
    public int Spacing { get; }

    public MiiEye(int type, int rotation, int vertical, MiiEyeColor color, int size, int spacing)
    {
        if (type is < 0 or > 47)
            throw new ArgumentException("Eye type invalid");
        if (rotation is < 0 or > 7)
            throw new ArgumentException("Rotation invalid");
        if (vertical is < 0 or > 18)
            throw new ArgumentException("Vertical position invalid");
        if (size is < 0 or > 7)
            throw new ArgumentException("Size invalid");
        if (spacing is < 0 or > 12)
            throw new ArgumentException("Spacing invalid");
        Type = type;
        Rotation = rotation;
        Vertical = vertical;
        Color = color;
        Size = size;
        Spacing = spacing;
    }

    public static OperationResult<MiiEye> Create(int type, int rotation, int vertical, MiiEyeColor color, int size, int spacing) =>
        TryCatch(() => new MiiEye(type, rotation, vertical, color, size, spacing));
}
