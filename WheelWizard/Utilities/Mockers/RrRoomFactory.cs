using WheelWizard.Models.RRInfo;
using WheelWizard.Utilities.Mockers.RrInfo;

namespace WheelWizard.Utilities.Mockers;

public class RrRoomFactory : MockingDataFactory<RrRoom, RrRoomFactory>
{
    protected override string DictionaryKeyGenerator(RrRoom value) => value.Id;
    private static int _roomCount = 1;

    public override RrRoom Create(int? seed = null)
    {
        var rand = Rand(seed);
        var playerCount = (int)(rand.NextDouble() * 12);
        var players = RrPlayerFactory.Instance.CreateAsDictionary(playerCount, seed); 
        var isPrivate = (int)(rand.NextDouble() * 3) == 0;
        return new RrRoom
        {
            Id = _roomCount++.ToString(),
            Game = "mariokartwii",
            Created = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30)),
            Type = isPrivate ? "private" : "public",
            Suspend = false,
            Host =  players.First().Value.Name,
            Rk = "vs_10", 
            Players = players
        };
    }
}
