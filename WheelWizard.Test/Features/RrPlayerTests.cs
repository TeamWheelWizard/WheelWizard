using WheelWizard.Models.RRInfo;

namespace WheelWizard.Test.Features;

public class RrPlayerTests
{
    [Fact]
    public void ConnectionQualityBars_ReturnsThree_WhenAmpThreeIsDominant()
    {
        var player = CreatePlayer("3", "3", "2", "0");

        Assert.Equal(3, player.ConnectionQualityBars);
    }

    [Fact]
    public void ConnectionQualityBars_ReturnsTwo_WhenAmpTwoIsDominant()
    {
        var player = CreatePlayer("2", "2", "1", "0");

        Assert.Equal(2, player.ConnectionQualityBars);
    }

    [Fact]
    public void ConnectionQualityBars_ReturnsOne_WhenOnlyWeakOrUnknownValuesExist()
    {
        var player = CreatePlayer("0", "1", "", "abc");

        Assert.Equal(1, player.ConnectionQualityBars);
    }

    [Fact]
    public void ConnectionQualityBars_ClampsToThree_WhenAmpIsAboveThree()
    {
        var player = CreatePlayer("4", "4", "0");

        Assert.Equal(3, player.ConnectionQualityBars);
    }

    private static RrPlayer CreatePlayer(params string[] connectionMap)
    {
        return new()
        {
            Pid = "1",
            Name = "Test",
            FriendCode = "0000-0000-0000",
            ConnectionMap = connectionMap.ToList(),
        };
    }
}
