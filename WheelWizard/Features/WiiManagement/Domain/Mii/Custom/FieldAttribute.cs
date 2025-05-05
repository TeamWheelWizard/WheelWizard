namespace WheelWizard.WiiManagement.Domain.Mii.Custom;

/// <summary>
/// This custom attribute is used to mark properties within the CustomMiiData class.
/// It specifies how many bits that property occupies within the packed 28-bit custom data payload.
/// </summary>
[AttributeUsage(AttributeTargets.Property)] // Specifies that this attribute can only be applied to properties.
internal sealed class BitFieldAttribute : Attribute
{
    /// <summary>
    /// Gets the number of bits allocated to the property decorated with this attribute.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="BitFieldAttribute"/> class.
    /// </summary>
    /// <param name="width">The number of bits the associated property will occupy.</param>
    public BitFieldAttribute(int width) => Width = width;
}
