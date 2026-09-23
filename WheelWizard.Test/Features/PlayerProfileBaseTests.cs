using WheelWizard.WiiManagement.GameLicense.Domain;

namespace WheelWizard.Test.Features;

public class PlayerProfileBaseTests
{
    private const uint InitialRating = 5000;

    public static TheoryData<uint> Ratings => new() { 0, 1, 4999, 5001, 65535, 65536, 543_210, 1_000_000 };

    private static FriendProfile CreateFriend() =>
        new()
        {
            FriendCode = "1234-5678-9012",
            Vr = InitialRating,
            Br = InitialRating,
            RegionId = 0,
            Mii = null,
            Wins = 0,
            Losses = 0,
            CountryCode = 0,
        };

    private static List<string?> TrackPropertyChanges(FriendProfile friend)
    {
        var changed = new List<string?>();
        friend.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Vr_RaisesPropertyChanged_WhenValueChanges(uint vr)
    {
        var friend = CreateFriend();
        var changed = TrackPropertyChanges(friend);

        friend.Vr = vr;

        Assert.Equal(vr, friend.Vr);
        Assert.Equal([nameof(FriendProfile.Vr)], changed);
    }

    [Theory]
    [MemberData(nameof(Ratings))]
    public void Br_RaisesPropertyChanged_WhenValueChanges(uint br)
    {
        var friend = CreateFriend();
        var changed = TrackPropertyChanges(friend);

        friend.Br = br;

        Assert.Equal(br, friend.Br);
        Assert.Equal([nameof(FriendProfile.Br)], changed);
    }

    [Fact]
    public void VrAndBr_DoNotRaisePropertyChanged_WhenValueIsTheSame()
    {
        var friend = CreateFriend();
        var changed = TrackPropertyChanges(friend);

        friend.Vr = InitialRating;
        friend.Br = InitialRating;

        Assert.Empty(changed);
    }
}
