namespace WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

/// <summary>
/// Structured wrapper around the 24 custom bits stored inside otherwise-unused Wii Mii fields.
/// Layout is fixed and explicit (no reflection):
/// bit 0-2: schema version
/// bit 3: copyable flag
/// bit 4-7: accent color
/// bit 8-10: preferred facial expression
/// bit 11-12: preferred camera angle
/// bit 13-17: preferred tagline
/// bit 18-23: spare
/// </summary>
public sealed class CustomMiiDataV1
{
    private const byte SchemaVersion = 1;
    private const byte VersionCycleLength = 7; // 3-bit schema cycles over 1-7
    private const int MaxMigrationHops = 5; // beyond this, data is treated as stale

    private const int TotalBits = 24;
    private const uint PayloadMask = (1u << TotalBits) - 1u;

    private const int VersionShift = 0;
    private const int IsCopyableShift = 3;
    private const int AccentColorShift = 4;
    private const int FacialExpressionShift = 8;
    private const int CameraAngleShift = 11;
    private const int TaglineShift = 13;
    private const int SpareShift = 18;

    private const int VersionWidth = 3;
    private const int IsCopyableWidth = 1;
    private const int AccentColorWidth = 4;
    private const int FacialExpressionWidth = 3;
    private const int CameraAngleWidth = 2;
    private const int TaglineWidth = 5;
    private const int SpareWidth = 6;

    private const uint VersionMask = ((1u << VersionWidth) - 1u) << VersionShift;
    private const uint IsCopyableMask = ((1u << IsCopyableWidth) - 1u) << IsCopyableShift;
    private const uint AccentColorMask = ((1u << AccentColorWidth) - 1u) << AccentColorShift;
    private const uint FacialExpressionMask = ((1u << FacialExpressionWidth) - 1u) << FacialExpressionShift;
    private const uint CameraAngleMask = ((1u << CameraAngleWidth) - 1u) << CameraAngleShift;
    private const uint TaglineMask = ((1u << TaglineWidth) - 1u) << TaglineShift;
    private const uint SpareMask = ((1u << SpareWidth) - 1u) << SpareShift;

    private uint _payload;

    private CustomMiiDataV1(uint payload) => _payload = payload & PayloadMask;

    public byte Version
    {
        get => (byte)ReadField(VersionMask, VersionShift, requiresSchemaTag: false);
        set => WriteField(value, VersionWidth, VersionMask, VersionShift, requiresSchemaTag: false);
    }

    /// <summary>
    /// True if this payload has a WheelWizard schema marker.
    /// Version 0 means "not authored/tagged by WheelWizard".
    /// </summary>
    public bool IsWheelWizardMii => Version != 0;

    public bool IsCopyable
    {
        get => ReadField(IsCopyableMask, IsCopyableShift) != 0;
        set => WriteField(value ? 1u : 0u, IsCopyableWidth, IsCopyableMask, IsCopyableShift);
    }

    public MiiProfileColor AccentColor
    {
        get => (MiiProfileColor)ReadField(AccentColorMask, AccentColorShift);
        set => WriteField((uint)value, AccentColorWidth, AccentColorMask, AccentColorShift);
    }

    public MiiPreferredFacialExpression FacialExpression
    {
        get => (MiiPreferredFacialExpression)ReadField(FacialExpressionMask, FacialExpressionShift);
        set => WriteField((uint)value, FacialExpressionWidth, FacialExpressionMask, FacialExpressionShift);
    }

    public MiiPreferredCameraAngle CameraAngle
    {
        get => (MiiPreferredCameraAngle)ReadField(CameraAngleMask, CameraAngleShift);
        set => WriteField((uint)value, CameraAngleWidth, CameraAngleMask, CameraAngleShift);
    }

    public MiiPreferredTagline Tagline
    {
        get => (MiiPreferredTagline)ReadField(TaglineMask, TaglineShift);
        set => WriteField((uint)value, TaglineWidth, TaglineMask, TaglineShift);
    }

    public ushort Spare
    {
        get => (ushort)ReadField(SpareMask, SpareShift);
        set => WriteField(value, SpareWidth, SpareMask, SpareShift);
    }

    /// <summary>
    /// Ensures this payload is WheelWizard-tagged.
    /// If untagged, it resets the payload to a clean schema-only state
    /// so any stray/legacy bits are discarded before further writes.
    /// </summary>
    /// <returns>True when the payload transitioned from untagged to tagged.</returns>
    public bool EnsureWheelWizardTag()
    {
        if (IsWheelWizardMii)
            return false;

        _payload = SchemaVersion;
        return true;
    }

    public static CustomMiiDataV1 FromBytes(byte[] rawMiiBytes) => FromPayload(CustomBitsCodec.Extract(rawMiiBytes));

    public static CustomMiiDataV1 FromMii(Mii mii)
    {
        var serializeResult = MiiSerializer.Serialize(mii);
        if (!serializeResult.IsSuccess)
            throw new InvalidOperationException("Failed to serialize Mii object to extract custom data.");
        return FromPayload(CustomBitsCodec.Extract(serializeResult.Value));
    }

    private static CustomMiiDataV1 FromPayload(uint rawPayload)
    {
        rawPayload &= PayloadMask;
        var diskVersion = (byte)(rawPayload & VersionMask);

        // Fast path: payload already matches our schema.
        if (diskVersion == SchemaVersion)
            return new(rawPayload);

        // Untagged/non-WheelWizard payload.
        if (diskVersion == 0)
            return CreateUnversioned();

        if (!TryComputeForwardDistance(diskVersion, SchemaVersion, out var distance))
            return CreateUnversioned();

        // If the payload is too many migrations behind, treat it as stale and discard.
        if (distance >= MaxMigrationHops)
            return CreateUnversioned();

        var migratedPayload = rawPayload;
        var currentVersion = diskVersion;

        for (var step = 0; step < distance; step++)
        {
            if (!TryMigrateFromVersion(currentVersion, migratedPayload, out var nextPayload))
                return CreateUnversioned();

            migratedPayload = nextPayload;
            currentVersion = NextVersion(currentVersion);
        }

        migratedPayload = WithVersion(migratedPayload, SchemaVersion);
        return new(migratedPayload);
    }

    /// <summary>
    /// Takes a payload encoded with "version N" and produces "version N+1".
    /// Keep each migration explicit when layouts change.
    /// </summary>
    private static bool TryMigrateFromVersion(byte oldVersion, uint oldPayload, out uint migratedPayload)
    {
        var nextVersion = NextVersion(oldVersion);

        switch (oldVersion)
        {
            case 1: // to version 2
                // No layout changes yet; simply bump the version.
                migratedPayload = WithVersion(oldPayload, nextVersion);
                return true;

            default:
                migratedPayload = oldPayload;
                return false;
        }
    }

    private static byte NextVersion(byte version) => version >= VersionCycleLength ? (byte)1 : (byte)(version + 1);

    private static bool TryComputeForwardDistance(byte fromVersion, byte targetVersion, out int distance)
    {
        distance = 0;

        if (fromVersion == 0 || targetVersion == 0)
            return false;

        var cursor = fromVersion;
        while (cursor != targetVersion && distance <= VersionCycleLength)
        {
            cursor = NextVersion(cursor);
            distance++;
        }

        return cursor == targetVersion;
    }

    private static uint WithVersion(uint payload, byte version) => (payload & ~VersionMask) | (version & VersionMask);

    public static CustomMiiDataV1 CreateEmpty() => new(SchemaVersion);

    private static CustomMiiDataV1 CreateUnversioned() => new(0);

    public Mii ApplyTo(Mii mii)
    {
        var serializeResult = MiiSerializer.Serialize(mii);
        if (!serializeResult.IsSuccess)
            throw new InvalidOperationException("Failed to serialize Mii object to apply custom data.");

        var bytes = serializeResult.Value;
        CustomBitsCodec.Inject(bytes, _payload);

        var deserializeResult = MiiSerializer.Deserialize(bytes);
        if (!deserializeResult.IsSuccess || deserializeResult.Value is null)
            throw new InvalidOperationException("Failed to deserialize Mii data after injecting custom payload.");

        return deserializeResult.Value;
    }

    public void ApplyTo(byte[] rawMiiBlock) => CustomBitsCodec.Inject(rawMiiBlock, _payload);

    private uint ReadField(uint mask, int shift, bool requiresSchemaTag = true)
    {
        if (requiresSchemaTag && !IsWheelWizardMii)
            return 0;
        return (_payload & mask) >> shift;
    }

    private void WriteField(uint value, int width, uint mask, int shift, bool requiresSchemaTag = true)
    {
        var maxValue = (1u << width) - 1u;
        if (value > maxValue)
            throw new ArgumentOutOfRangeException(nameof(value), $"Value {value} exceeds the {width}-bit limit (max {maxValue}).");

        if (requiresSchemaTag)
            EnsureWheelWizardTag();

        _payload = (_payload & ~mask) | ((value << shift) & mask);
        _payload &= PayloadMask;
    }
}
