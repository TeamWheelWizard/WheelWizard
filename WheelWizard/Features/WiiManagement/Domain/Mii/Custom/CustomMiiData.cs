using System.Reflection;
using System.Runtime.CompilerServices;

namespace WheelWizard.WiiManagement.Domain.Mii.Custom;

/// <summary>
/// Provides a structured way to access and modify the 28 "unknown" or unused bits
/// found within the standard 74-byte Mii data format. This allows storing custom data
/// without breaking compatibility with standard Mii readers.
///
/// The 28 bits are laid out as follows by this class:
/// • Bits 0–2  : Schema version. This helps manage different layouts of the custom data over time.
/// • Bits 3–27 : Available for custom fields defined as properties below. These are automatically
///               packed based on their declaration order and specified [BitField] width.
/// </summary>
public sealed class CustomMiiData
{
    // Defines the current version of the custom data layout.
    // Increment this (1-7, then loop) if the layout of properties below changes,
    // allowing older versions to recognize or ignore newer data formats.
    private const int SchemaVersion = 1;

    #region Layout Metadata

    // The total number of bits available for custom data within the Mii format's unused sections.
    // This is a hard limit, Never change this!
    private const int TotalBits = 28;

    // Stores the packed custom data as a 32-bit unsigned integer.
    // Only the lower 28 bits are used. Bit 0 is the least significant bit (LSB).
    private uint _payload;

    // A helper record to store metadata about each property marked with [BitField].
    // Prop: The reflection PropertyInfo object itself.
    // Offset: The starting bit position of this field within the 28-bit payload (0-27).
    // Width: The number of bits allocated to this field.
    // Mask: A pre-calculated bitmask to easily isolate/find or clear this field's bits within the payload.
    private sealed record FieldMeta(PropertyInfo Prop, int Offset, int Width, uint Mask);

    // A dictionary mapping property names (e.g., "Version", "IsCopyable") to their calculated metadata.
    // This is built once using reflection in the static constructor.
    private static readonly IReadOnlyDictionary<string, FieldMeta> _meta;

    // Static constructor: This runs once when the CustomMiiData class is first used.
    // Its purpose is to automatically determine the layout of the custom bit fields.
    static CustomMiiData()
    {
        // Get all public and non-public instance properties of this class.
        var properties = typeof(CustomMiiData).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        // Filter to find only properties that have the [BitField] attribute.
        var bitFieldProperties = properties.Where(p => p.GetCustomAttribute<BitFieldAttribute>() is not null);

        // Order these properties based on their declaration order in the source code.
        // MetadataToken provides a stable ordering. This ensures bits are packed consistently.
        var orderedProperties = bitFieldProperties.OrderBy(p => p.MetadataToken).ToArray();

        // Prepare a list to hold the metadata for each field.
        var fieldMetadataList = new List<FieldMeta>();
        // Initialize a cursor to keep track of the next available bit position.
        var currentBitOffset = 0;

        // Iterate through the ordered properties to calculate their position and mask.
        foreach (var prop in orderedProperties)
        {
            // Get the width specified in the [BitField] attribute for this property.
            var width = prop.GetCustomAttribute<BitFieldAttribute>()!.Width;

            // Validate the specified width.
            if (width is <= 0 or > TotalBits)
                throw new InvalidOperationException($"Bit width for '{prop.Name}' must be between 1 and {TotalBits}.");

            // Check if adding this property exceeds the total available bits.
            if (currentBitOffset + width > TotalBits)
                throw new InvalidOperationException(
                    $"Layout overflow – Adding '{prop.Name}' ({width} bits) exceeds the {TotalBits}-bit budget. Current offset: {currentBitOffset}."
                );

            // Calculate the bitmask for this field.
            // 1. Create a value with 'width' number of 1s (e.g., width 3 -> 0b111).
            //    This is done by shifting 1 left by 'width' (1 << width gives 0b1000 for width 3)
            //    and subtracting 1 ((1u << width) - 1u gives 0b0111).
            // 2. Shift this mask left by 'currentBitOffset' to position it correctly within the payload.
            //    e.g., if offset is 5 and width is 3, mask is 0b0111 << 5 = 0b11100000.
            var mask = ((1u << width) - 1u) << currentBitOffset;

            // Create the metadata record for this property and add it to the list.
            fieldMetadataList.Add(new FieldMeta(prop, currentBitOffset, width, mask));

            // Advance the cursor by the width of the current field.
            currentBitOffset += width;
        }

        // if not all 28 bits were used by the defined properties.
        if (currentBitOffset < TotalBits)
        {
            //for now do nothing but we could add stuff here
        }

        // Store the generated metadata list as a dictionary, keyed by property name for easy lookup.
        _meta = fieldMetadataList.ToDictionary(m => m.Prop.Name, m => m);
    }

    #endregion

    // ──────────────────────────────────────────  PROPERTIES ──────────────────────────────────────────
    // GO HERE IF YOU WANT TO CHANGE THE CUSTOM MII DATA WITHOUT WORRYING ABOUT THE INTERNALS!!!!!!!!!!!

    /// <summary>
    /// Gets or sets the 3-bit schema version (bits 0-2).
    /// This should always be the first field defined.
    /// The setter is private to ensure its only set internally (e.g., in CreateEmpty).
    /// </summary>
    [BitField(3)]
    public byte Version
    {
        get => (byte)GetField();
        set => SetField(value);
    }

    /// <summary>
    /// Gets or sets whether the Mii is allowed to be copied from other consoles (1 bit).
    /// Uses a boolean for easy access, mapping to 1 (true) or 0 (false).
    /// </summary>
    [BitField(1)]
    public bool IsCopyable
    {
        get => GetField() != 0; // Read the 1-bit value; non-zero means true.
        set => SetField(value ? 1u : 0u); // Write 1 if true, 0 if false.
    }

    /// <summary>
    /// Gets or sets a sample 4-bit colour value (0-15).
    /// The width could be changed if needed.
    /// </summary>
    [BitField(4)]
    public MiiProfileColor AccentColor
    {
        get => (MiiProfileColor)GetField();
        set => SetField((uint)value);
    }

    /// <summary>
    /// Gets or sets eight individual feature flags packed into 3 bits
    /// </summary>
    [BitField(3)]
    public MiiPreferredFacialExpression FacialExpression
    {
        get => (MiiPreferredFacialExpression)GetField();
        set => SetField((uint)value);
    }

    [BitField(2)]
    public MiiPreferredCameraAngle CameraAngle
    {
        get => (MiiPreferredCameraAngle)GetField();
        set => SetField((uint)value);
    }

    [BitField(5)]
    public MiiPreferredTagline Tagline
    {
        get => (MiiPreferredTagline)GetField();
        set => SetField((uint)value);
    }

    // Add new properties here.
    // Simply declare them with a [BitField(width)] attribute.
    // They will be automatically allocated space in the payload after the 'Spare' field,
    // provided the total width does not exceed 28 bits. The static constructor handles layout.

    [BitField(10)]
    public ushort Spare
    {
        get => (ushort)GetField();
        set => SetField(value);
    }

    #region Constructors

    /// <summary>
    /// Private constructor used internally to create an instance with a given payload.
    /// </summary>
    /// <param name="payload">The packed 28-bit data.</param>
    private CustomMiiData(uint payload) => _payload = payload;

    /// <summary>
    /// Creates a <see cref="CustomMiiData"/> instance by extracting the 28 custom bits
    /// from a raw 74-byte Mii data block.
    /// </summary>
    public static CustomMiiData FromBytes(byte[] rawMiiBytes)
    {
        var result = CustomBitsCodec.Extract(rawMiiBytes);
        return FromPayload(result);
    }

    /// <summary>
    /// Creates a <see cref="CustomMiiData"/> instance by extracting the 28 custom bits
    /// from a <see cref="Mii"/> object.
    /// This involves serializing the Mii object to bytes first.
    /// </summary>
    public static CustomMiiData FromMii(Mii mii)
    {
        var serializeResult = MiiSerializer.Serialize(mii);
        if (!serializeResult.IsSuccess)
            throw new InvalidOperationException("Failed to serialize Mii object to extract custom data.");
        return FromPayload(CustomBitsCodec.Extract(serializeResult.Value));
    }

    private static CustomMiiData FromPayload(uint rawpayload)
    {
        var versionMask = (1u << 3) - 1u;
        var diskVersion = (byte)(rawpayload & versionMask);

        // If it’s exactly what we expect, we’re done:
        if (diskVersion == SchemaVersion)
            return new(rawpayload);

        if (diskVersion == 0)
            return new(rawpayload);

        // If it’s an older, supported version, migrate it forward one step at a time:
        // ReSharper disable once ConditionIsAlwaysTrueOrFalse
        if (diskVersion >= SchemaVersion) // don't remove this, once version goes above 1 this check is not redundant anymore
            return CreateEmpty();

        var migrated = MigrateFromVersion(rawpayload, diskVersion);
        return FromPayload(migrated);
    }

    /// <summary>
    /// Takes a payload encoded with “version N” and produces a payload encoded with “version N+1”.
    /// Each case handles the bit‐shuffling or defaulting needed to move from one schema to the next.
    /// </summary>
    /// <param name="oldPayload">The 28‐bit data block from an older schema version.</param>
    /// <param name="oldVersion">The schema version of that payload.</param>
    private static uint MigrateFromVersion(uint oldPayload, byte oldVersion)
    {
        switch (oldVersion)
        {
            case 1: // to version 2
                return oldPayload;

            // case 2: // to version 3
            //     // Migration logic for version 3 goes here...
            //     break;

            default:
                return CreateEmpty()._payload;
        }
    }

    /// <summary>
    /// Creates a new <see cref="CustomMiiData"/> instance with all custom bits initially set to zero,
    /// but with the <see cref="Version"/> field automatically set to the current <see cref="SchemaVersion"/>.
    /// </summary>
    /// <returns>A new, default-initialized <see cref="CustomMiiData"/> instance.</returns>
    public static CustomMiiData CreateEmpty() => new(0) { Version = SchemaVersion };

    #endregion

    #region SerializeMethods

    /// <summary>
    /// Applies the current custom data payload (_payload) to a given <see cref="Mii"/> object.
    /// This method returns a *new* Mii object instance with the changes applied, leaving the original untouched.
    /// </summary>
    /// <param name="mii">The original Mii object.</param>
    public Mii ApplyTo(Mii mii)
    {
        // Serialize the input Mii to get its byte representation.
        // The serializer should handle cloning or creating a safe copy.
        var serializeResult = MiiSerializer.Serialize(mii);
        if (!serializeResult.IsSuccess)
            throw new InvalidOperationException("Failed to serialize Mii object to apply custom data."); // Or handle error
        var bytes = serializeResult.Value;
        CustomBitsCodec.Inject(bytes, _payload);
        var deserializeResult = MiiSerializer.Deserialize(bytes);
        if (!deserializeResult.IsSuccess || deserializeResult.Value is null)
            throw new InvalidOperationException("Failed to deserialize Mii data after injecting custom payload."); // Or handle error

        return deserializeResult.Value;
    }

    /// <summary>
    /// Injects the current custom data payload (_payload) directly into a raw 74-byte Mii data block.
    /// This method modifies the provided byte array in place.
    /// </summary>
    public void ApplyTo(byte[] rawMiiBlock) => CustomBitsCodec.Inject(rawMiiBlock, _payload);
    #endregion

    #region HelperMethods


    /// <summary>
    /// Gets the value of the property that called this method.
    /// It uses reflection metadata (_meta) to find the correct bits in the payload.
    /// </summary>
    /// <param name="propName">The name of the calling property, automatically supplied by the compiler.</param>
    /// <returns>The value of the requested field, extracted from the _payload.</returns>
    private uint GetField([CallerMemberName] string? propName = null)
    {
        // Look up the metadata (offset, width, mask) for the property.
        var meta = _meta[propName!]; // Assumes propName is always valid due to CallerMemberName.

        // Apply the mask to the payload to isolate the bits for this field.
        // (e.g., payload & 0b11100000 isolates bits 5-7 if that's the mask).
        var isolatedBits = _payload & meta.Mask;

        // Right-shift the isolated bits by the field's offset to align them to the LSB.
        // (e.g., if isolatedBits is 0b10100000 and offset is 5, result is 0b101).
        return isolatedBits >> meta.Offset;
    }

    /// <summary>
    /// Sets the value of the property that called this method.
    /// It uses reflection metadata (_meta) to place the value into the correct bits in the payload.
    /// </summary>
    /// <param name="value">The value to set for the field.</param>
    /// <param name="propName">The name of the calling property, automatically supplied by the compiler.</param>
    private void SetField(uint value, [CallerMemberName] string? propName = null)
    {
        if (propName != nameof(Version) && GetField(nameof(Version)) == 0)
        {
            SetField(SchemaVersion, nameof(Version));
        }
        // Look up the metadata for the property.
        var meta = _meta[propName!];

        // Validate that the provided value fits within the allocated number of bits.
        // Calculate the maximum possible value for the given width (2^width - 1).
        var maxValue = (1u << meta.Width) - 1;
        if (value > maxValue)
        {
            // ReSharper disable once LocalizableElement
            throw new ArgumentOutOfRangeException(propName, $"Value {value} exceeds the {meta.Width}-bit limit (max {maxValue}).");
        }

        // Update the payload:
        // 1. Clear the bits for this field in the current payload using the inverted mask.
        //    (e.g., _payload & ~0b11100000 clears bits 5-7).
        var clearedPayload = _payload & ~meta.Mask;

        // 2. Left-shift the new value by the field's offset to position it correctly.
        //    (e.g., if value is 0b101 and offset is 5, shifted value is 0b10100000).
        var shiftedValue = value << meta.Offset;

        // 3. Combine the cleared payload with the shifted new value using bitwise OR.
        //    (e.g., clearedPayload | 0b10100000 inserts the new value).
        _payload = clearedPayload | shiftedValue;
    }
    #endregion
}
