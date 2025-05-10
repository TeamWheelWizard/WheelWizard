namespace WheelWizard.WiiManagement.Domain.Mii;

public class MiiLip
{
    public int Type { get; }
    public MiiLipColor Color { get; }
    public int Size { get; }
    public int Vertical { get; }

    public MiiLip(int type, MiiLipColor color, int size, int vertical)
    {
        if (type is < 0 or > 23)
            throw new ArgumentException("Lip type invalid");
        if (size is < 0 or > 8)
            throw new ArgumentException("Lip size invalid");
        if (vertical is < 0 or > 18)
            throw new ArgumentException("Lip vertical position invalid");

        Type = type;
        Color = color;
        Size = size;
        Vertical = vertical;
    }

    public static OperationResult<MiiLip> Create(int type, MiiLipColor color, int size, int vertical) =>
        TryCatch(() => new MiiLip(type, color, size, vertical));
}
