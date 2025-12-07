namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

public class MiiMole
{
    public bool Exists { get; }
    public int Size { get; }
    public int Vertical { get; }
    public int Horizontal { get; }

    public MiiMole(bool exists, int size, int vertical, int horizontal)
    {
        if (size is < 0 or > 8)
            throw new ArgumentException("Mole size invalid");
        if (vertical is < 0 or > 30)
            throw new ArgumentException("Mole vertical position invalid");
        if (horizontal is < 0 or > 16)
            throw new ArgumentException("Mole horizontal position invalid");
        Exists = exists;
        Size = size;
        Vertical = vertical;
        Horizontal = horizontal;
    }

    public static OperationResult<MiiMole> Create(bool exists, int size, int vertical, int horizontal) =>
        TryCatch(() => new MiiMole(exists, size, vertical, horizontal));
}
