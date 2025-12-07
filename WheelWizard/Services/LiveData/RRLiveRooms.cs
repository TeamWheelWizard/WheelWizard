using WheelWizard.Models.RRInfo;
using WheelWizard.RrRooms;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Services.LiveData;

public class RRLiveRooms : RepeatedTaskManager
{
    public List<RrRoom> CurrentRooms { get; private set; } = [];
    public int PlayerCount => CurrentRooms.Sum(room => room.PlayerCount);
    public int RoomCount => CurrentRooms.Count;

    private static RRLiveRooms? _instance;
    public static RRLiveRooms Instance => _instance ??= new();

    private RRLiveRooms()
        : base(40) { }

    protected override async Task ExecuteTaskAsync()
    {
        var whWzService = App.Services.GetRequiredService<IWhWzDataSingletonService>();
        var roomsService = App.Services.GetRequiredService<IRrRoomsSingletonService>();

        var roomsResult = await roomsService.GetRoomsAsync();
        if (roomsResult.IsFailure)
        {
            CurrentRooms = [];
            return;
        }

        //source: https://kevinvg207.github.io/rr-rooms/
        // 1) split any “accidentally merged” rooms
        var raw = roomsResult.Value;
        var splitRaw = SplitMergedRooms(raw);

        var rrRooms = splitRaw
            .Select(room => new RrRoom
            {
                Id = room.Id,
                Game = room.Game,
                Created = room.Created,
                Type = room.Type,
                Suspend = room.Suspend,
                Host = room.Host,
                Rk = room.Rk,
                Players = room.Players.ToDictionary(
                    kv => kv.Key,
                    kv =>
                    {
                        var p = kv.Value;
                        return new RrPlayer
                        {
                            Count = p.Count,
                            Pid = p.Pid,
                            Name = p.Name,
                            ConnMap = p.ConnMap,
                            ConnFail = p.ConnFail,
                            Suspend = p.Suspend,
                            Fc = p.Fc,
                            Ev = p.Ev,
                            Eb = p.Eb,
                            BadgeVariants = whWzService.GetBadges(p.Fc),
                            Mii = p
                                .Mii.Select(mii =>
                                {
                                    var bytes = Convert.FromBase64String(mii.Data);
                                    var des = MiiSerializer.Deserialize(bytes);
                                    return des.IsSuccess ? des.Value : new Mii();
                                })
                                .ToList(),
                        };
                    }
                ),
            })
            .ToList();

        CurrentRooms = rrRooms;
    }

    private static List<RwfcRoom> SplitMergedRooms(List<RwfcRoom> rooms)
    {
        var output = new List<RwfcRoom>();

        foreach (var room in rooms)
        {
            var keys = room.Players.Keys.ToList();
            var n = keys.Count;

            // build adjacency of “two‐way” connections
            var adj = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();

            for (var i = 0; i < n; i++)
            {
                var map = room.Players[keys[i]].ConnMap;
                for (var j = 0; j < map.Length; j++)
                {
                    if (map[j] == '0')
                        continue;

                    var other = j >= i ? j + 1 : j;
                    // only add if we’ll later see the reverse link
                    adj[i].Add(other);
                }
            }

            // find connected components
            var seen = new bool[n];
            var components = new List<List<int>>();

            for (var i = 0; i < n; i++)
            {
                if (seen[i])
                    continue;
                var stack = new Stack<int>();
                stack.Push(i);

                var comp = new List<int>();
                while (stack.Count > 0)
                {
                    var u = stack.Pop();
                    if (seen[u])
                        continue;
                    seen[u] = true;
                    comp.Add(u);

                    foreach (var v in adj[u].Where(v => adj[v].Contains(u)))
                    {
                        stack.Push(v);
                    }
                }

                comp.Sort();
                components.Add(comp);
            }

            // if it’s really merged, split it
            if (components.Count > 1)
            {
                output.AddRange(
                    components.Select(comp => new RwfcRoom
                    {
                        Id = room.Id,
                        Game = room.Game,
                        Created = room.Created,
                        Type = room.Type,
                        Suspend = room.Suspend,
                        Host = room.Host,
                        Rk = room.Rk,
                        Players = comp.ToDictionary(idx => keys[idx], idx => room.Players[keys[idx]]),
                    })
                );
            }
            else
            {
                // nothing to do
                output.Add(room);
            }
        }

        return output;
    }
}
