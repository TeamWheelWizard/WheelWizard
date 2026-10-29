using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Features.WiiManagement;

public class FriendRatingTests
{
    [Theory]
    [InlineData(293u, 29_300u)] // Written by Retro Rewind for 29,367 VR
    [InlineData(1u, 100u)]
    [InlineData(10_000u, 1_000_000u)] // Highest rating
    [InlineData(5000u, 5000u)] // Default written by older WheelWizard versions
    [InlineData(29_367u, 29_367u)] // Full points written by older WheelWizard versions
    public void FriendRatingFromRksys_ReturnsRatingPoints(uint stored, uint points)
    {
        Assert.Equal(points, GameLicenseSingletonService.FriendRatingFromRksys(stored));
    }

    [Theory]
    [InlineData(29_367u, 293)]
    [InlineData(5000u, 50)]
    [InlineData(2_000_000u, 10_000)] // Above the highest rating
    public void FriendRatingToRksys_StoresWholeHundreds(uint points, ushort stored)
    {
        Assert.Equal(stored, GameLicenseSingletonService.FriendRatingToRksys(points));
    }

    [Fact]
    public void FriendRating_RoundTripsDefaultRating()
    {
        var stored = GameLicenseSingletonService.FriendRatingToRksys(5000);

        Assert.Equal(5000u, GameLicenseSingletonService.FriendRatingFromRksys(stored));
    }
}
