using System.Linq.Expressions;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.Test.Features.RrRooms;

public class RrBadgeTests
{
    // Real pid / friend code pairs from the RWFC leaderboard.
    private const string Pid1 = "198760529";
    private const string FriendCode1 = "3137-3137-3137";
    private const string Pid2 = "4544000";
    private const string FriendCode2 = "0000-0454-4000";

    private readonly IApiCaller<IRwfcApi> _apiCaller;
    private readonly RrBadgeSingletonService _service;

    public RrBadgeTests()
    {
        _apiCaller = Substitute.For<IApiCaller<IRwfcApi>>();
        _service = new(_apiCaller);
    }

    private void SetupResponse(Dictionary<string, int[]?> badges) =>
        _apiCaller
            .CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcBadgesResponse>>>>())
            .Returns(Ok(new RwfcBadgesResponse { Badges = badges }));

    [Fact]
    public async Task LoadBadgesAsync_MapsProfileIdsToFriendCodes()
    {
        SetupResponse(new() { { Pid1, [1, 1001] }, { Pid2, [2003] } });

        var result = await _service.LoadBadgesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal([BadgeVariant.WheelWizardDeveloper, BadgeVariant.Translator], _service.GetBadges(FriendCode1));
        Assert.Equal([BadgeVariant.LeafStruckGold], _service.GetBadges(FriendCode2));
    }

    [Fact]
    public async Task LoadBadgesAsync_ReturnsFailure_WhenApiCallFails()
    {
        var expectedError = new OperationError { Message = "API call failed" };
        _apiCaller.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<RwfcBadgesResponse>>>>()).Returns(expectedError);

        var result = await _service.LoadBadgesAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(expectedError.Message, result.Error.Message);
        Assert.Empty(_service.GetBadges(FriendCode1));
    }

    [Fact]
    public async Task LoadBadgesAsync_SortsAndDeduplicatesBadges()
    {
        SetupResponse(new() { { Pid1, [2015, 1002, 100, 1002] } });

        await _service.LoadBadgesAsync();

        Assert.Equal([BadgeVariant.RWFCModerator, BadgeVariant.Supporter, BadgeVariant.EarthboundGold], _service.GetBadges(FriendCode1));
    }

    [Fact]
    public async Task LoadBadgesAsync_SkipsUnknownBadgesAndInvalidEntries()
    {
        SetupResponse(
            new()
            {
                { Pid1, [-1, 0, 999, 9999] },
                { Pid2, null },
                { "not-a-pid", [0] },
                { "0", [0] },
            }
        );

        var result = await _service.LoadBadgesAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal([BadgeVariant.RetroRewindDeveloper], _service.GetBadges(FriendCode1));
        Assert.Empty(_service.GetBadges(FriendCode2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a friend code")]
    [InlineData("3137-3137-3138")] // checksum does not match the pid
    [InlineData("0000-0000-0000")]
    public async Task GetBadges_ReturnsEmpty_ForUnknownOrInvalidFriendCodes(string friendCode)
    {
        SetupResponse(new() { { Pid1, [1] } });
        await _service.LoadBadgesAsync();

        Assert.Empty(_service.GetBadges(friendCode));
    }

    [Fact]
    public async Task LoadBadgesAsync_ReplacesPreviousBadges()
    {
        SetupResponse(new() { { Pid1, [1] } });
        await _service.LoadBadgesAsync();
        Assert.Equal([BadgeVariant.WheelWizardDeveloper], _service.GetBadges(FriendCode1));

        SetupResponse(new() { { Pid2, [1001] } });
        await _service.LoadBadgesAsync();

        Assert.Empty(_service.GetBadges(FriendCode1));
        Assert.Equal([BadgeVariant.Translator], _service.GetBadges(FriendCode2));
    }
}
