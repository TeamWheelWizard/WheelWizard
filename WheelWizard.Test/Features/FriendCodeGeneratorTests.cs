using WheelWizard.Utilities.Generators;

namespace WheelWizard.Test.Features;

public class FriendCodeGeneratorTests
{
    [Fact]
    public void FriendCodeToProfileId_ReturnsProfileId_ForValidFriendCode()
    {
        const string friendCode = "3484-8484-8484";

        var profileId = FriendCodeGenerator.FriendCodeToProfileId(friendCode);

        Assert.Equal(592497508u, profileId);
    }

    [Fact]
    public void FriendCodeToProfileId_ReturnsZero_ForChecksumMismatch()
    {
        const string invalidFriendCode = "0005-9249-7508";

        var profileId = FriendCodeGenerator.FriendCodeToProfileId(invalidFriendCode);

        Assert.Equal(0u, profileId);
    }

    [Fact]
    public void ProfileIdToFriendCode_ReturnsExpectedFriendCodeValue()
    {
        const uint profileId = 592497508;

        var friendCodeValue = FriendCodeGenerator.ProfileIdToFriendCode(profileId);

        Assert.Equal(348484848484ul, friendCodeValue);
    }
}
