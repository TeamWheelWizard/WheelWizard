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
        return new()
        {
            Pid = playerId.ToString(),
            Name = $"Player {playerId}",
            FriendCode = FriendCodeFactory.Instance.Create(),
            Vr = (int)(rand.NextDouble() * 99999),
            Br = (int)(rand.NextDouble() * 9999),
            IsOpenHost = false,
            IsSuspended = false,
            ConnectionMap = ["0"],
            Mii = MiiFactory.Instance.Create(seed),
        };
    }
}
