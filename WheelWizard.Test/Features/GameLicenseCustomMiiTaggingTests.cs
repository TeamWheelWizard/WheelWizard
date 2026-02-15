using System.IO.Abstractions;
using System.Reflection;
using WheelWizard.Helpers;
using WheelWizard.Shared;
using WheelWizard.Utilities.Generators;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii.Custom;

namespace WheelWizard.Test.Features;

public class GameLicenseCustomMiiTaggingTests
{
    private const int RksysSize = 0x2BC000;
    private const int RkpdOffsetForUser0 = 0x08;

    [Fact]
    public void ParseLicenseUser_UntaggedMii_IsTaggedAndPersisted()
    {
        var miiDb = Substitute.For<IMiiDbService>();
        var fileSystem = Substitute.For<IFileSystem>();
        var whWzData = Substitute.For<IWhWzDataSingletonService>();
        var rrRating = Substitute.For<IRRratingReader>();
        whWzData.GetBadges(Arg.Any<string>()).Returns([]);

        var mii = MiiFactory.CreateDefaultFemale();
        mii.CustomDataV1.AccentColor = MiiProfileColor.Color5;
        mii.CustomDataV1.Version = 0; // simulate untagged payload with stale bits

        miiDb.GetByAvatarId(Arg.Any<uint>()).Returns(Ok(mii));
        miiDb.Update(Arg.Any<Mii>()).Returns(Ok());

        var sut = new GameLicenseSingletonService(miiDb, fileSystem, whWzData, rrRating);
        var rksys = BuildRksysWithAvatarId(mii.MiiId);

        var result = InvokeParseLicenseUser(sut, rksys);

        Assert.True(result.IsSuccess);
        Assert.True(mii.CustomDataV1.IsWheelWizardMii);
        Assert.Equal(1, mii.CustomDataV1.Version);
        Assert.Equal(MiiProfileColor.None, mii.CustomDataV1.AccentColor); // rogue bits sanitized
        miiDb.Received(1).Update(mii);
    }

    [Fact]
    public void ParseLicenseUser_AlreadyTaggedMii_DoesNotPersistAgain()
    {
        var miiDb = Substitute.For<IMiiDbService>();
        var fileSystem = Substitute.For<IFileSystem>();
        var whWzData = Substitute.For<IWhWzDataSingletonService>();
        var rrRating = Substitute.For<IRRratingReader>();
        whWzData.GetBadges(Arg.Any<string>()).Returns([]);

        var mii = MiiFactory.CreateDefaultMale();
        mii.CustomDataV1.IsCopyable = true; // already tagged

        miiDb.GetByAvatarId(Arg.Any<uint>()).Returns(Ok(mii));
        miiDb.Update(Arg.Any<Mii>()).Returns(Ok());

        var sut = new GameLicenseSingletonService(miiDb, fileSystem, whWzData, rrRating);
        var rksys = BuildRksysWithAvatarId(mii.MiiId);

        var result = InvokeParseLicenseUser(sut, rksys);

        Assert.True(result.IsSuccess);
        Assert.True(mii.CustomDataV1.IsWheelWizardMii);
        Assert.True(mii.CustomDataV1.IsCopyable);
        miiDb.DidNotReceive().Update(Arg.Any<Mii>());
    }

    [Fact]
    public void ParseLicenseUser_UpdateFailure_RevertsInMemoryToUnversioned()
    {
        var miiDb = Substitute.For<IMiiDbService>();
        var fileSystem = Substitute.For<IFileSystem>();
        var whWzData = Substitute.For<IWhWzDataSingletonService>();
        var rrRating = Substitute.For<IRRratingReader>();
        whWzData.GetBadges(Arg.Any<string>()).Returns([]);

        var mii = MiiFactory.CreateDefaultFemale();
        mii.CustomDataV1.Tagline = MiiPreferredTagline.Tagline12;
        mii.CustomDataV1.Version = 0;

        miiDb.GetByAvatarId(Arg.Any<uint>()).Returns(Ok(mii));
        miiDb.Update(Arg.Any<Mii>()).Returns(Fail("update failed"));

        var sut = new GameLicenseSingletonService(miiDb, fileSystem, whWzData, rrRating);
        var rksys = BuildRksysWithAvatarId(mii.MiiId);

        var result = InvokeParseLicenseUser(sut, rksys);

        Assert.True(result.IsSuccess);
        Assert.False(mii.CustomDataV1.IsWheelWizardMii);
        Assert.Equal(0, mii.CustomDataV1.Version);
        Assert.Equal(MiiPreferredTagline.None, mii.CustomDataV1.Tagline);
        miiDb.Received(1).Update(mii);
    }

    private static byte[] BuildRksysWithAvatarId(uint avatarId)
    {
        var rksys = new byte[RksysSize];
        BigEndianBinaryHelper.WriteUInt32BigEndian(rksys, RkpdOffsetForUser0 + 0x28, avatarId);
        return rksys;
    }

    private static OperationResult<LicenseProfile> InvokeParseLicenseUser(GameLicenseSingletonService service, byte[] rksys)
    {
        var type = typeof(GameLicenseSingletonService);
        var dataField = type.GetField("_rksysData", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(dataField);
        dataField!.SetValue(service, rksys);

        var parseMethod = type.GetMethod("ParseLicenseUser", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(parseMethod);

        var result = parseMethod!.Invoke(service, [RkpdOffsetForUser0]);
        Assert.NotNull(result);
        return Assert.IsType<OperationResult<LicenseProfile>>(result);
    }
}
