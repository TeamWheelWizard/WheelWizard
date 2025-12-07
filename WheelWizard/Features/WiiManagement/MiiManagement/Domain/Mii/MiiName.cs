using System.Text;

namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

/// <summary>
/// Represents a Mii name.
/// </summary>
public class MiiName
{
    private readonly string _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="MiiName"/> class with the specified value.
    /// </summary>
    /// <param name="value">The Mii name value.</param>
    /// <exception cref="ArgumentException">Mii name cannot be empty or longer than 10 characters.</exception>
    public MiiName(string value)
    {
        //Mii names are allowed to be empty since creators can be empty
        if (value == null)
            throw new ArgumentException("Mii name cannot be null");

        if (value.Length > 10)
            throw new ArgumentException("Mii name too long, maximum is 10 characters");
        _value = value;
    }

    /// <summary>
    /// Creates a new instance of the <see cref="MiiName"/> class with the specified value.
    /// </summary>
    /// <param name="value">The Mii name value.</param>
    /// <returns>An <see cref="OperationResult{MiiName}"/> representing the result of the operation.</returns>
    public static OperationResult<MiiName> Create(string value) => TryCatch(() => new MiiName(value));

    public byte[] ToBytes() => Encoding.BigEndianUnicode.GetBytes(_value.PadRight(10, '\0'));

    public static MiiName FromBytes(byte[] data, int offset) => new(Encoding.BigEndianUnicode.GetString(data, offset, 20).TrimEnd('\0'));

    public override string ToString() => _value;
}
