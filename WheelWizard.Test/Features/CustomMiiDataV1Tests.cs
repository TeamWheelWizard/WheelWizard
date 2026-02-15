using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

namespace WheelWizard.Test.Features;

public class CustomMiiDataV1Tests
{
    [Fact]
    public void EnsureWheelWizardTag_ClearsRogueBits_WhenPayloadWasUnversioned()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.AccentColor = MiiProfileColor.Color6;
        data.Tagline = MiiPreferredTagline.Tagline7;

        // Simulate legacy/rogue payload: clear only the version bits.
        data.Version = 0;

        var changed = data.EnsureWheelWizardTag();

        Assert.True(changed);
        Assert.Equal(1, data.Version);
        Assert.Equal(MiiProfileColor.None, data.AccentColor);
        Assert.Equal(MiiPreferredTagline.None, data.Tagline);
        Assert.False(data.IsCopyable);
    }

    [Fact]
    public void SettingCustomFieldOnUnversionedPayload_AutoTagsAndSanitizes()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.AccentColor = MiiProfileColor.Color3;

        // Simulate unversioned payload while keeping stale non-version bits around.
        data.Version = 0;
        data.Tagline = MiiPreferredTagline.Tagline10;

        Assert.Equal(1, data.Version);
        Assert.Equal(MiiPreferredTagline.Tagline10, data.Tagline);
        Assert.Equal(MiiProfileColor.None, data.AccentColor);
    }

    [Fact]
    public void SerializeDeserialize_RoundTrip_PreservesExplicitCustomFields()
    {
        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.IsCopyable = true;
        mii.CustomDataV1.AccentColor = MiiProfileColor.Color11;
        mii.CustomDataV1.FacialExpression = MiiPreferredFacialExpression.FacialExpression5;
        mii.CustomDataV1.CameraAngle = MiiPreferredCameraAngle.CameraAngle3;
        mii.CustomDataV1.Tagline = MiiPreferredTagline.Tagline14;
        mii.CustomDataV1.Spare = 42;

        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        var deserialized = MiiSerializer.Deserialize(serialized.Value);
        Assert.True(deserialized.IsSuccess);

        var roundTrip = deserialized.Value;
        Assert.True(roundTrip.CustomDataV1.IsWheelWizardMii);
        Assert.True(roundTrip.CustomDataV1.IsCopyable);
        Assert.Equal(MiiProfileColor.Color11, roundTrip.CustomDataV1.AccentColor);
        Assert.Equal(MiiPreferredFacialExpression.FacialExpression5, roundTrip.CustomDataV1.FacialExpression);
        Assert.Equal(MiiPreferredCameraAngle.CameraAngle3, roundTrip.CustomDataV1.CameraAngle);
        Assert.Equal(MiiPreferredTagline.Tagline14, roundTrip.CustomDataV1.Tagline);
        Assert.Equal<ushort>(42, roundTrip.CustomDataV1.Spare);
    }

    [Fact]
    public void EnsureWheelWizardTag_IsIdempotent_ForAlreadyTaggedPayload()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.IsCopyable = true;
        data.AccentColor = MiiProfileColor.Color9;
        data.Tagline = MiiPreferredTagline.Tagline3;

        var changed = data.EnsureWheelWizardTag();

        Assert.False(changed);
        Assert.Equal(1, data.Version);
        Assert.True(data.IsCopyable);
        Assert.Equal(MiiProfileColor.Color9, data.AccentColor);
        Assert.Equal(MiiPreferredTagline.Tagline3, data.Tagline);
    }

    [Fact]
    public void SerializeDeserialize_UnsupportedSchemaVersion2_IsIgnoredAsNonWheelWizard()
    {
        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.IsCopyable = true;
        mii.CustomDataV1.AccentColor = MiiProfileColor.Color15;
        mii.CustomDataV1.Version = 2;

        var serialized = MiiSerializer.Serialize(mii);
        Assert.True(serialized.IsSuccess);

        var deserialized = MiiSerializer.Deserialize(serialized.Value);
        Assert.True(deserialized.IsSuccess);

        var custom = deserialized.Value.CustomDataV1;
        Assert.False(custom.IsWheelWizardMii);
        Assert.Equal(0, custom.Version);
        Assert.False(custom.IsCopyable);
        Assert.Equal(MiiProfileColor.None, custom.AccentColor);
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

        Assert.Throws<ArgumentOutOfRangeException>(() => data.Version = 8);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Spare = 64);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.CameraAngle = (MiiPreferredCameraAngle)4);
    }

    [Fact]
    public void Setters_AcceptMaximumRepresentableValues()
    {
        var data = CustomMiiDataV1.CreateEmpty();
        data.Version = 7;
        data.IsCopyable = true;
        data.AccentColor = MiiProfileColor.Color15;
        data.FacialExpression = MiiPreferredFacialExpression.FacialExpression7;
        data.CameraAngle = MiiPreferredCameraAngle.CameraAngle3;
        data.Tagline = MiiPreferredTagline.Tagline31;
        data.Spare = 63;

        Assert.Equal(7, data.Version);
        Assert.True(data.IsCopyable);
        Assert.Equal(MiiProfileColor.Color15, data.AccentColor);
        Assert.Equal(MiiPreferredFacialExpression.FacialExpression7, data.FacialExpression);
        Assert.Equal(MiiPreferredCameraAngle.CameraAngle3, data.CameraAngle);
        Assert.Equal(MiiPreferredTagline.Tagline31, data.Tagline);
        Assert.Equal<ushort>(63, data.Spare);
    }
}
