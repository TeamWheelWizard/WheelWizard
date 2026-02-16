using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

namespace WheelWizard.Test.Features;

public class CustomMiiDataV1Tests
{
    private const byte CurrentSchemaVersion = 1;

    [Fact]
    public void EnsureWheelWizardTag_ClearsRogueBits_WhenPayloadWasUnversioned()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.FacialExpression = MiiPreferredFacialExpression.FacialExpression6;
        data.CameraAngle = MiiPreferredCameraAngle.CameraAngle2;
        data.Tagline = MiiPreferredTagline.Tagline7;

        // Simulate legacy/rogue payload: clear only the version bits.
        data.Version = 0;

        var changed = data.EnsureWheelWizardTag();

        Assert.True(changed);
        Assert.Equal(CurrentSchemaVersion, data.Version);
        Assert.Equal(MiiPreferredFacialExpression.None, data.FacialExpression);
        Assert.Equal(MiiPreferredCameraAngle.None, data.CameraAngle);
        Assert.Equal(MiiPreferredTagline.None, data.Tagline);
        Assert.False(data.IsCopyable);
        Assert.Equal(0, data.SpareBits);
    }

    [Fact]
    public void SettingCustomFieldOnUnversionedPayload_AutoTagsAndSanitizes()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.CameraAngle = MiiPreferredCameraAngle.CameraAngle3;

        // Simulate unversioned payload while keeping stale non-version bits around.
        data.Version = 0;
        data.Tagline = MiiPreferredTagline.Tagline10;

        Assert.Equal(CurrentSchemaVersion, data.Version);
        Assert.Equal(MiiPreferredTagline.Tagline10, data.Tagline);
        Assert.Equal(MiiPreferredCameraAngle.None, data.CameraAngle);
        Assert.Equal(0, data.SpareBits);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_PreservesExplicitCustomFields()
    {
        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.IsCopyable = true;
        mii.CustomDataV1.FacialExpression = MiiPreferredFacialExpression.FacialExpression5;
        mii.CustomDataV1.CameraAngle = MiiPreferredCameraAngle.CameraAngle3;
        mii.CustomDataV1.Tagline = MiiPreferredTagline.Tagline14;

        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        var deserialized = MiiSerializer.Deserialize(serialized.Value);
        Assert.True(deserialized.IsSuccess);

        var roundTrip = deserialized.Value;
        Assert.True(roundTrip.CustomDataV1.IsWheelWizardMii);
        Assert.True(roundTrip.CustomDataV1.IsCopyable);
        Assert.Equal(MiiPreferredFacialExpression.FacialExpression5, roundTrip.CustomDataV1.FacialExpression);
        Assert.Equal(MiiPreferredCameraAngle.CameraAngle3, roundTrip.CustomDataV1.CameraAngle);
        Assert.Equal(MiiPreferredTagline.Tagline14, roundTrip.CustomDataV1.Tagline);
        Assert.Equal<ushort>(21, roundTrip.CustomDataV1.Reserved);
        Assert.Equal(0, roundTrip.CustomDataV1.SpareBits);
    }

    [Fact]
    public void EnsureWheelWizardTag_IsIdempotent_ForAlreadyTaggedPayload()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.IsCopyable = true;
        data.FacialExpression = MiiPreferredFacialExpression.FacialExpression3;
        data.Tagline = MiiPreferredTagline.Tagline3;

        var changed = data.EnsureWheelWizardTag();

        Assert.False(changed);
        Assert.Equal(CurrentSchemaVersion, data.Version);
        Assert.True(data.IsCopyable);
        Assert.Equal(MiiPreferredFacialExpression.FacialExpression3, data.FacialExpression);
        Assert.Equal(MiiPreferredTagline.Tagline3, data.Tagline);
        Assert.Equal(0, data.SpareBits);
    }

    [Fact]
    public void SerializeDeserialize_UnsupportedSchemaVersion2_IsIgnoredAsNonWheelWizard()
    {
        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.IsCopyable = true;
        mii.CustomDataV1.Tagline = MiiPreferredTagline.Tagline5;
        mii.CustomDataV1.Version = 2;

        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        var deserialized = MiiSerializer.Deserialize(serialized.Value);
        Assert.True(deserialized.IsSuccess);

        var custom = deserialized.Value.CustomDataV1;
        Assert.False(custom.IsWheelWizardMii);
        Assert.Equal(0, custom.Version);
        Assert.False(custom.IsCopyable);
        Assert.Equal(MiiPreferredTagline.None, custom.Tagline);
        Assert.Equal(0, custom.SpareBits);
    }

    [Fact]
    public void SerializeDeserialize_UnsupportedSchemaVersion7_IsIgnoredAsNonWheelWizard()
    {
        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.FacialExpression = MiiPreferredFacialExpression.FacialExpression4;
        mii.CustomDataV1.CameraAngle = MiiPreferredCameraAngle.CameraAngle2;
        mii.CustomDataV1.Version = 7;

        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        var deserialized = MiiSerializer.Deserialize(serialized.Value);
        Assert.True(deserialized.IsSuccess);

        var custom = deserialized.Value.CustomDataV1;
        Assert.False(custom.IsWheelWizardMii);
        Assert.Equal(0, custom.Version);
        Assert.Equal(MiiPreferredFacialExpression.None, custom.FacialExpression);
        Assert.Equal(MiiPreferredCameraAngle.None, custom.CameraAngle);
    }

    [Fact]
    public void Setters_EnforceBitWidthLimits()
    {
        var data = CustomMiiDataV1.CreateEmpty();

        Assert.Throws<ArgumentOutOfRangeException>(() => data.Version = 16);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.CameraAngle = (MiiPreferredCameraAngle)4);
    }

    [Fact]
    public void Setters_AcceptMaximumRepresentableValues()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.Version = 15;
        Assert.Equal(15, data.Version);
    }

    [Fact]
    public void SerializeDeserialize_VersionMatchesSchemaButMarkerMissing_IsIgnored()
    {
        var mii = MiiFactory.CreateDefaultMale();
        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        // Force version nibble to the schema value while clearing reserved marker bits.
        // This simulates a wild payload collision where version appears valid by chance.
        var raw = serialized.Value;
        var face = (ushort)((raw[0x20] << 8) | raw[0x21]);
        var eye = (uint)((raw[0x28] << 24) | (raw[0x29] << 16) | (raw[0x2A] << 8) | raw[0x2B]);
        var nose = (ushort)((raw[0x2C] << 8) | raw[0x2D]);
        var mole = (ushort)((raw[0x34] << 8) | raw[0x35]);

        const ushort faceVersionMask = 0x003A; // u0 bits (3..5) + u1 bit (1)
        face = (ushort)(face & ~faceVersionMask);
        face |= (ushort)(1 << 3); // 0b001 (version low 3 bits for nibble 1)

        eye &= ~0x10u; // clear payload bit19 (u7 bit4)
        nose &= 0xFFF8; // clear payload bits20-22 (u8 bits0-2)
        mole &= 0xFFFE; // clear payload bit23 (u10 bit0)

        raw[0x20] = (byte)(face >> 8);
        raw[0x21] = (byte)face;
        raw[0x28] = (byte)(eye >> 24);
        raw[0x29] = (byte)(eye >> 16);
        raw[0x2A] = (byte)(eye >> 8);
        raw[0x2B] = (byte)eye;
        raw[0x2C] = (byte)(nose >> 8);
        raw[0x2D] = (byte)nose;
        raw[0x34] = (byte)(mole >> 8);
        raw[0x35] = (byte)mole;

        var deserialized = MiiSerializer.Deserialize(raw);
        Assert.True(deserialized.IsSuccess);

        var custom = deserialized.Value.CustomDataV1;
        Assert.False(custom.IsWheelWizardMii);
        Assert.Equal(0, custom.Version);
    }
}
