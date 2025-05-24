namespace WheelWizard.WiiManagement.Domain.Mii;

public class MiiGlasses
{
    public MiiGlassesType Type { get; }
    public MiiGlassesColor Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    public MiiGlasses(MiiGlassesType type, MiiGlassesColor color, int size, int vertical)
    {
        if (size is < 0 or > 7)
            throw new ArgumentException("Glasses size invalid");
        if (vertical is < 0 or > 20)
            throw new ArgumentException("Glasses vertical position invalid");
        Type = type;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiGlasses> Create(MiiGlassesType type, MiiGlassesColor color, int size, int vertical) =>
        TryCatch(() => new MiiGlasses(type, color, size, vertical));
}
