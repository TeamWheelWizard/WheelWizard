using System.Text;
using WheelWizard.Shared.Binary;
using WheelWizard.WiiManagement.FriendCodes;

namespace WheelWizard.Test.Features.WiiManagement;

public class FriendCodeTests
{
    [Fact]
    public void FriendCodeToProfileId_ReturnsProfileId_ForValidFriendCode()
    {
        const string friendCode = "3484-8484-8484";

        var profileId = FriendCode.FriendCodeToProfileId(friendCode);

        Assert.Equal(592497508u, profileId);
    }

    [Fact]
    public void FriendCodeToProfileId_ReturnsZero_ForChecksumMismatch()
    {
        const string invalidFriendCode = "0005-9249-7508";

        var profileId = FriendCode.FriendCodeToProfileId(invalidFriendCode);

        Assert.Equal(0u, profileId);
    }

    [Fact]
    public void ProfileIdToFriendCode_ReturnsExpectedFriendCodeValue()
    {
        const uint profileId = 592497508;

        var friendCodeValue = FriendCode.ProfileIdToFriendCode(profileId);

        Assert.Equal(348484848484ul, friendCodeValue);
    }

    [Fact]
    public void ComputeCrc8_MatchesTheStandardCheckValue()
    {
        var data = Encoding.ASCII.GetBytes("123456789");

        Assert.Equal(0xF4, Crc.ComputeCrc8(data, 0, data.Length));
    }
}
