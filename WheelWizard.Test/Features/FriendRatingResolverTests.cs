using WheelWizard.Models.RRInfo;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.Test.Features;

public class FriendRatingResolverTests
{
    private const string FriendCode = "1234-5678-9012";
    private const string OtherFriendCode = "0000-0000-0001";
    private const uint MaxRating = 1_000_000;
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0);

    // VR/BR pairs spread over the whole valid range, including 16-bit and default-rating edges
    public static TheoryData<uint, uint> Ratings =>
        new()
        {
            { 1, 1 },
            { 99, 100 },
            { 4999, 5000 },
            { 5000, 5001 },
            { 9999, 10000 },
            { 65535, 65536 },
            { 100_001, 99_999 },
            { 543_210, 876_543 },
            { MaxRating, MaxRating },
        };

    // What the game writes to rksys.dat for a given rating (integer part of rating / 100)
    private static uint GameRksysValue(uint rating) => rating / 100;

    private static FriendProfile CreateFriend(uint vr, uint br, string friendCode = FriendCode) =>
        new()
        {
            FriendCode = friendCode,
            Vr = vr,
            Br = br,
            RegionId = 0,
            Mii = null,
            Wins = 0,
            Losses = 0,
            CountryCode = 0,
        };

    private static FriendProfile CreateStaleFriend(uint vr, uint br, string friendCode = FriendCode) =>
        CreateFriend(GameRksysValue(vr), GameRksysValue(br), friendCode);

    private static RrPlayer CreatePlayer(int? vr, int? br, string friendCode = FriendCode) =>
        new()
        {
            Pid = "1",
            Name = "Player",
            FriendCode = friendCode,
            Vr = vr,
            Br = br,
        };

    #region BrFromRksys

    [Fact]
    public void BrFromRksys_HandlesEveryPossibleRksysValue()
    {
        for (uint rawBr = 0; rawBr <= ushort.MaxValue; rawBr++)
        {
            var br = FriendRatingResolver.BrFromRksys(rawBr);

            if (rawBr == FriendRatingResolver.DefaultFriendRating || rawBr > FriendRatingResolver.MaxRksysFriendRating)
                Assert.Equal(rawBr, br);
            else
            {
                Assert.Equal(rawBr * 100, br);
                Assert.InRange(br, 0u, MaxRating);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void BrFromRksys_RestoresGameWrittenBr_ToWithin100Points(uint _, uint br)
    {
        var rawBr = GameRksysValue(br);
        if (rawBr == FriendRatingResolver.DefaultFriendRating)
            return; // Ambiguous with the default rating, covered separately

        var restored = FriendRatingResolver.BrFromRksys(rawBr);

        Assert.InRange(restored, br - br % 100, br);
    }

    [Fact]
    public void BrFromRksys_KeepsDefaultRating()
    {
        Assert.Equal(FriendRatingResolver.DefaultFriendRating, FriendRatingResolver.BrFromRksys(FriendRatingResolver.DefaultFriendRating));
    }

    #endregion

    #region VrFromApiProfile

    [Theory]
    [MemberData(nameof(Ratings))]
    public void VrFromApiProfile_ReturnsVr_ForAnyValidRating(uint vr, uint _)
    {
        Assert.Equal(vr, FriendRatingResolver.VrFromApiProfile(new() { Vr = (int)vr }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void VrFromApiProfile_ReturnsNull_ForNonPositiveVr(int vr)
    {
        Assert.Null(FriendRatingResolver.VrFromApiProfile(new() { Vr = vr }));
    }

    [Fact]
    public void VrFromApiProfile_ReturnsNull_ForNullProfile()
    {
        Assert.Null(FriendRatingResolver.VrFromApiProfile(null));
    }

    #endregion

    #region Apply - live rooms

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_UsesLiveVrAndBr_ForOnlineFriend(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateStaleFriend(vr, br);

        var toFetch = resolver.Apply([friend], [CreatePlayer((int)vr, (int)br)], Now);

        Assert.Equal(vr, friend.Vr);
        Assert.Equal(br, friend.Br);
        Assert.Empty(toFetch);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_KeepsBr_WhenLivePlayerHasNoBr(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateFriend(GameRksysValue(vr), br);

        resolver.Apply([friend], [CreatePlayer((int)vr, null)], Now);

        Assert.Equal(vr, friend.Vr);
        Assert.Equal(br, friend.Br);
    }

    [Fact]
    public void Apply_ClampsNegativeLiveValuesToZero()
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateFriend(FriendRatingResolver.DefaultFriendRating, FriendRatingResolver.DefaultFriendRating);

        resolver.Apply([friend], [CreatePlayer(-1, -1)], Now);

        Assert.Equal(0u, friend.Vr);
        Assert.Equal(0u, friend.Br);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_RequestsApiVr_WhenLivePlayerHasNoVr(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateStaleFriend(vr, br);

        var toFetch = resolver.Apply([friend], [CreatePlayer(null, (int)br)], Now);

        Assert.Equal(GameRksysValue(vr), friend.Vr);
        Assert.Equal([FriendCode], toFetch);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_IgnoresLivePlayersWithOtherFriendCodes(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateStaleFriend(vr, br);

        var toFetch = resolver.Apply([friend], [CreatePlayer((int)vr, (int)br, OtherFriendCode)], Now);

        Assert.Equal(GameRksysValue(vr), friend.Vr);
        Assert.Equal(GameRksysValue(br), friend.Br);
        Assert.Equal([FriendCode], toFetch);
    }

    [Fact]
    public void Apply_UsesFirstLivePlayer_WhenFriendCodeIsDuplicated()
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateFriend(1, 1);

        resolver.Apply([friend], [CreatePlayer(1234, 5678), CreatePlayer(8765, 4321)], Now);

        Assert.Equal(1234u, friend.Vr);
        Assert.Equal(5678u, friend.Br);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_KeepsLiveRatings_AfterFriendGoesOffline(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        resolver.Apply([CreateStaleFriend(vr, br)], [CreatePlayer((int)vr, (int)br)], Now);

        // The license service re-creates friends from rksys.dat on every reload
        var reloadedFriend = CreateStaleFriend(vr, br);
        var toFetch = resolver.Apply([reloadedFriend], [], Now.AddMinutes(1));

        Assert.Equal(vr, reloadedFriend.Vr);
        Assert.Equal(br, reloadedFriend.Br);
        Assert.Empty(toFetch);
    }

    #endregion

    #region Apply - API cache

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_RequestsApiVr_AndKeepsRksysValues_ForOfflineFriendWithoutCache(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateStaleFriend(vr, br);

        var toFetch = resolver.Apply([friend], [], Now);

        Assert.Equal([FriendCode], toFetch);
        Assert.Equal(GameRksysValue(vr), friend.Vr);
        Assert.Equal(GameRksysValue(br), friend.Br);
    }

    [Fact]
    public void Apply_DoesNotRequestAgain_WhileRequestIsPending()
    {
        var resolver = new FriendRatingResolver();

        resolver.Apply([CreateFriend(1, 1)], [], Now);
        var toFetch = resolver.Apply([CreateFriend(1, 1)], [], Now);

        Assert.Empty(toFetch);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_UsesCachedApiVr_AndKeepsBr_WithinCacheDuration(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        resolver.Apply([CreateStaleFriend(vr, br)], [], Now);
        resolver.StoreApiVr(FriendCode, vr, Now);

        var friend = CreateStaleFriend(vr, br);
        var toFetch = resolver.Apply([friend], [], Now + FriendRatingResolver.ApiVrCacheDuration - TimeSpan.FromSeconds(1));

        Assert.Equal(vr, friend.Vr);
        Assert.Equal(GameRksysValue(br), friend.Br);
        Assert.Empty(toFetch);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_RequestsApiVrAgain_AfterCacheExpires_AndKeepsCachedValueMeanwhile(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        resolver.Apply([CreateStaleFriend(vr, br)], [], Now);
        resolver.StoreApiVr(FriendCode, vr, Now);

        var friend = CreateStaleFriend(vr, br);
        var toFetch = resolver.Apply([friend], [], Now + FriendRatingResolver.ApiVrCacheDuration);

        Assert.Equal(vr, friend.Vr);
        Assert.Equal([FriendCode], toFetch);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Apply_KeepsRksysVr_AndDoesNotRetry_AfterFailedRequest(uint vr, uint br)
    {
        var resolver = new FriendRatingResolver();
        resolver.Apply([CreateStaleFriend(vr, br)], [], Now);
        resolver.StoreApiVr(FriendCode, null, Now);

        var friend = CreateStaleFriend(vr, br);
        var toFetch = resolver.Apply([friend], [], Now.AddMinutes(1));

        Assert.Equal(GameRksysValue(vr), friend.Vr);
        Assert.Empty(toFetch);
    }

    [Fact]
    public void Apply_RetriesFailedRequest_AfterCacheExpires()
    {
        var resolver = new FriendRatingResolver();
        resolver.Apply([CreateFriend(1, 1)], [], Now);
        resolver.StoreApiVr(FriendCode, null, Now);

        var toFetch = resolver.Apply([CreateFriend(1, 1)], [], Now + FriendRatingResolver.ApiVrCacheDuration);

        Assert.Equal([FriendCode], toFetch);
    }

    #endregion

    [Fact]
    public void Apply_SkipsFriendsWithoutFriendCode()
    {
        var resolver = new FriendRatingResolver();
        var friend = CreateFriend(1, 1, friendCode: "");

        var toFetch = resolver.Apply([friend], [CreatePlayer(1234, 5678, friendCode: "")], Now);

        Assert.Equal(1u, friend.Vr);
        Assert.Equal(1u, friend.Br);
        Assert.Empty(toFetch);
    }

    [Fact]
    public void Apply_ResolvesEachFriendIndependently()
    {
        var resolver = new FriendRatingResolver();
        var onlineFriend = CreateFriend(1, 1);
        var offlineFriend = CreateFriend(2, 2, OtherFriendCode);

        var toFetch = resolver.Apply([onlineFriend, offlineFriend], [CreatePlayer(1234, 5678)], Now);

        Assert.Equal(1234u, onlineFriend.Vr);
        Assert.Equal(5678u, onlineFriend.Br);
        Assert.Equal(2u, offlineFriend.Vr);
        Assert.Equal(2u, offlineFriend.Br);
        Assert.Equal([OtherFriendCode], toFetch);
    }
}
