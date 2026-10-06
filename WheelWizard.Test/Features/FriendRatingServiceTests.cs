using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.Test.Features;

public class FriendRatingServiceTests
{
    [Fact]
    public async Task ApiResultSurvivesFriendReloadAndDoesNotRequestAgainWithinCacheDuration()
    {
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        var response = new TaskCompletionSource<OperationResult<PlayerProfileResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>()).Returns(response.Task);
        var ratings = new FriendRatingService(api, TimeProvider.System, NullLogger<FriendRatingService>.Instance);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ratings.RatingsChanged += () => changed.TrySetResult();

        ratings.Refresh([CreateFriend()], []);
        response.SetResult(Ok(new PlayerProfileResponse { Vr = 29367 }));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var reloaded = CreateFriend();
        ratings.Refresh([reloaded], []);

        Assert.Equal(29367u, reloaded.Vr);
        await api.Received(1).CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>());
    }

    private static FriendProfile CreateFriend() =>
        new()
        {
            FriendCode = "1234-5678-9012",
            Vr = 293,
            Br = 5000,
            RegionId = 0,
            Mii = null,
            Wins = 0,
            Losses = 0,
            CountryCode = 0,
        };
}
