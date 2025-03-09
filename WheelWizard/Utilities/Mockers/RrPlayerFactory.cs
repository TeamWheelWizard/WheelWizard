using WheelWizard.Models.RRInfo;

namespace WheelWizard.Utilities.Mockers.RrInfo;

public class RrPlayerFactory : MockingDataFactory<RrPlayer, RrPlayerFactory>
{
    protected override string DictionaryKeyGenerator(RrPlayer value) => value.Name;
    private static int _playerCount = 1;
    
    public override RrPlayer Create(int? seed = null)
    {
        var playerId = _playerCount++;
        var rand = Rand(seed);
        return new RrPlayer
        {
            Count = "1",
            Pid = playerId.ToString(),
            Name = $"Player {playerId}",
            ConnMap = "0",
            ConnFail = "0",
            Suspend = "0",
            Fc = FriendCodeFactory.Instance.Create(),
            Ev = ((int)(rand.NextDouble() * 9999)).ToString(),
            Eb = ((int)(rand.NextDouble() * 9999)).ToString(),
            Mii = MiiFactory.Instance.CreateMultiple(1, seed).ToList()
        };
    }
}
