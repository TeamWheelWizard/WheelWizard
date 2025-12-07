namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

public class MiiScale
{
    public byte Value { get; }

    public MiiScale(byte value)
    {
        if (value > 127)
            throw new ArgumentException("Scale must be between 0 and 127.");
        Value = value;
    }

    public static OperationResult<MiiScale> Create(byte value) => TryCatch(() => new MiiScale(value));
}
