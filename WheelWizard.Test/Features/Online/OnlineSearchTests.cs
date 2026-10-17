using System.Linq.Expressions;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.Models;
using WheelWizard.Online.ViewModels;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Polling;
using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Features.Online;

public class OnlineSearchTests
{
    [Fact]
    public async Task Search_FindsOfflineProfileByFriendCode_AndClearingCancelsPendingSearch()
    {
        var leaderboard = Substitute.For<IRrLeaderboardSingletonService>();
        leaderboard.GetTopPlayersAsync(50).Returns(OperationResult.Ok(new List<RwfcLeaderboardEntry>()));
        var api = Substitute.For<IApiCaller<IRwfcApi>>();
        api.CallApiAsync(Arg.Any<Expression<Func<IRwfcApi, Task<PlayerProfileResponse>>>>())
            .Returns(
                OperationResult.Ok(
                    new PlayerProfileResponse
                    {
                        Name = "Offline racer",
                        FriendCode = "1234-5678-9012",
                        Vr = 5000,
                    }
                )
            );
        using var rooms = new LiveRoomsService(
            Substitute.For<IWhWzDataSingletonService>(),
            Substitute.For<IRrRoomsSingletonService>(),
            leaderboard,
            Substitute.For<IGameLicenseSingletonService>(),
            new RoomPresence(),
            Substitute.For<IPollingScheduler>(),
            TimeProvider.System,
            NullLogger<LiveRoomsService>.Instance
        );
        var model = new OnlineViewModel(rooms, leaderboard, api);

        await model.SearchAsync("123456789012");
        var player = Assert.Single(model.Results);
        Assert.Equal("Offline racer", player.Name);
        Assert.False(player.IsOnline);
        Assert.False(model.IsLoading);

        var pending = model.SearchAsync("Offline");
        await model.SearchAsync("");
        await pending;
        Assert.False(model.IsSearching);
        Assert.False(model.IsLoading);
        Assert.Empty(model.Results);
        model.Deactivate();
    }
}
