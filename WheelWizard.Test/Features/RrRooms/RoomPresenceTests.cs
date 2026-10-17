using System.IO.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.RrRooms;
using WheelWizard.Settings;
using WheelWizard.Shared.Polling;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features.RrRooms;

public class RoomPresenceTests
{
    [Fact]
    public void PresenceReplacement_DoesNotRetainPlayersOrShareOwners()
    {
        var presence = new RoomPresence();
        var other = new RoomPresence();
        presence.Replace(["1111-2222-3333", "1111-2222-3333", ""]);
        Assert.True(presence.IsOnline("1111-2222-3333"));
        Assert.False(other.IsOnline("1111-2222-3333"));
        Assert.False(presence.IsOnline(""));
        presence.Replace([]);
        Assert.False(presence.IsOnline("1111-2222-3333"));
    }

    [Fact]
    public void Refresh_UpdatesBothLicenseAndFriends_AndOnlyNotifiesChanges()
    {
        var presence = new RoomPresence();
        var service = new GameLicenseSingletonService(
            Substitute.For<IMiiDbService>(),
            Substitute.For<IFileSystem>(),
            Substitute.For<IWhWzDataSingletonService>(),
            Substitute.For<IRrRatingReader>(),
            Substitute.For<ISettingsManager>(),
            Substitute.For<ISaveRegionService>(),
            Substitute.For<IDolphinPaths>(),
            Substitute.For<ICustomDistributionPaths>(),
            presence,
            Substitute.For<IPollingScheduler>(),
            TimeProvider.System,
            NullLogger<GameLicenseSingletonService>.Instance
        );
        var user = new LicenseProfile
        {
            FriendCode = "user",
            Vr = 5000,
            Br = 5000,
            RegionId = 0,
            Mii = null,
            TotalRaceCount = 0,
            TotalWinCount = 0,
        };
        var friend = new FriendProfile
        {
            FriendCode = "friend",
            Vr = 5000,
            Br = 5000,
            RegionId = 0,
            Mii = null,
            Wins = 0,
            Losses = 0,
            CountryCode = 0,
        };
        user.Friends.Add(friend);
        service.LicenseCollection.Users.Add(user);
        var notifications = 0;
        user.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(user.IsOnline))
                notifications++;
        };
        friend.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(friend.IsOnline))
                notifications++;
        };

        presence.Replace(["user", "friend"]);
        service.RefreshOnlineStatus();
        service.RefreshOnlineStatus();

        Assert.True(user.IsOnline);
        Assert.True(friend.IsOnline);
        Assert.Equal(2, notifications);

        presence.Replace([]);
        service.RefreshOnlineStatus();
        Assert.False(user.IsOnline);
        Assert.False(friend.IsOnline);
        Assert.Equal(4, notifications);
    }

    [Fact]
    public async Task FailedRoomRefresh_ClearsPresenceBeforeRefreshingProfiles()
    {
        var presence = new RoomPresence();
        var rooms = Substitute.For<IRrRoomsSingletonService>();
        var leaderboard = Substitute.For<IRrLeaderboardSingletonService>();
        leaderboard.GetTopPlayersAsync(50).Returns(Ok(new List<RwfcLeaderboardEntry>()));
        var licenses = Substitute.For<IGameLicenseSingletonService>();
        licenses.ActiveCurrentFriends.Returns([]);
        var service = new TestRooms(Substitute.For<IWhWzDataSingletonService>(), rooms, leaderboard, licenses, presence);
        rooms
            .GetRoomsAsync()
            .Returns(
                Ok(
                    new List<RwfcRoomStatusRoom>
                    {
                        new()
                        {
                            Id = "room",
                            Type = "public",
                            Created = DateTime.UtcNow,
                            Players = [new() { Pid = "1", FriendCode = "1111-2222-3333" }],
                        },
                    }
                )
            );

        await service.Refresh();
        Assert.Equal(1, service.PlayerCount);
        Assert.True(presence.IsOnline("1111-2222-3333"));
        licenses.ClearReceivedCalls();
        licenses.When(owner => owner.RefreshOnlineStatus()).Do(_ => Assert.False(presence.IsOnline("1111-2222-3333")));
        rooms.GetRoomsAsync().Returns((WheelWizard.Shared.OperationResult<List<RwfcRoomStatusRoom>>)Fail("unavailable"));

        await service.Refresh();

        Assert.Empty(service.CurrentRooms);
        licenses.Received(1).RefreshOnlineStatus();
    }

    [Fact]
    public async Task RaceMetadata_SurvivesRoomSplitting_AndClearsWhenAbsent()
    {
        const string json = """
            {"rooms":[{"id":"room","type":"anybody","created":"2026-10-17T09:00:00Z",
            "race":{"num":12,"course":359,"cc":2,"trackName":"Wii Rainbow Road"},
            "players":[{"pid":"1","connectionMap":["0","1","0"]},
                       {"pid":"2","connectionMap":["1","0","0"]},
                       {"pid":"3","connectionMap":["0","0","0"]}]}]}
            """;
        var response = System.Text.Json.JsonSerializer.Deserialize<RwfcRoomStatusResponse>(
            json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        )!;
        var rooms = Substitute.For<IRrRoomsSingletonService>();
        rooms.GetRoomsAsync().Returns(Ok(response.Rooms));
        var leaderboard = Substitute.For<IRrLeaderboardSingletonService>();
        leaderboard.GetTopPlayersAsync(50).Returns(Ok(new List<RwfcLeaderboardEntry>()));
        var licenses = Substitute.For<IGameLicenseSingletonService>();
        licenses.ActiveCurrentFriends.Returns([]);
        using var service = new TestRooms(Substitute.For<IWhWzDataSingletonService>(), rooms, leaderboard, licenses, new RoomPresence());

        await service.Refresh();

        Assert.Equal(2, service.CurrentRooms.Count);
        Assert.All(
            service.CurrentRooms,
            room =>
            {
                Assert.Equal(359, room.Race!.Course);
                Assert.Equal("Race 12 · Wii Rainbow Road", room.RaceDescription);
            }
        );

        response.Rooms[0].Race = null;
        await service.Refresh();
        Assert.All(service.CurrentRooms, room => Assert.Equal("No race reported", room.RaceDescription));
    }

    private sealed class TestRooms(
        IWhWzDataSingletonService data,
        IRrRoomsSingletonService rooms,
        IRrLeaderboardSingletonService leaderboard,
        IGameLicenseSingletonService licenses,
        IRoomPresence presence
    )
        : LiveRoomsService(
            data,
            rooms,
            leaderboard,
            licenses,
            presence,
            Substitute.For<IPollingScheduler>(),
            TimeProvider.System,
            NullLogger<LiveRoomsService>.Instance
        )
    {
        public Task Refresh() => ExecuteTaskAsync(CancellationToken.None);
    }
}
