using WheelWizard.Models.RRInfo;
using WheelWizard.RrRooms;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Services.LiveData;

public class RRLiveRooms : RepeatedTaskManager
{
    private readonly IWhWzDataSingletonService _whWzService;
    private readonly IRrRoomsSingletonService _roomsService;
    private readonly IRrLeaderboardSingletonService _leaderboardService;
    private readonly IGameLicenseSingletonService _gameLicenseService;

    public List<RrRoom> CurrentRooms { get; private set; } = [];
    public int PlayerCount => CurrentRooms.Sum(room => room.PlayerCount);
    public int RoomCount => CurrentRooms.Count;

    public static RRLiveRooms Instance => App.Services.GetRequiredService<RRLiveRooms>();

    public RRLiveRooms(
        IWhWzDataSingletonService whWzService,
        IRrRoomsSingletonService roomsService,
        IRrLeaderboardSingletonService leaderboardService,
        IGameLicenseSingletonService gameLicenseService
    )
        : base(40)
    {
        _whWzService = whWzService;
        _roomsService = roomsService;
        _leaderboardService = leaderboardService;
        _gameLicenseService = gameLicenseService;
    }

    protected override async Task ExecuteTaskAsync()
    {
        var roomsTask = _roomsService.GetRoomsAsync();
        var leaderboardTask = _leaderboardService.GetTopPlayersAsync(50);

        await Task.WhenAll(roomsTask, leaderboardTask);

        var roomsResult = roomsTask.Result;
        var leaderboardResult = leaderboardTask.Result;
        if (roomsResult.IsFailure)
        {
            CurrentRooms = [];
            return;
        }

        var leaderboardByPid = leaderboardResult.IsSuccess
            ? leaderboardResult
                .Value.Where(entry => !string.IsNullOrWhiteSpace(entry.Pid))
                .GroupBy(entry => entry.Pid, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            : new Dictionary<string, RwfcLeaderboardEntry>(StringComparer.Ordinal);

        var leaderboardByFriendCode = leaderboardResult.IsSuccess
            ? leaderboardResult
                .Value.Where(entry => !string.IsNullOrWhiteSpace(entry.FriendCode))
                .GroupBy(entry => entry.FriendCode, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal)
            : new Dictionary<string, RwfcLeaderboardEntry>(StringComparer.Ordinal);

        //source: https://kevinvg207.github.io/rr-rooms/
        // 1) split any “accidentally merged” rooms
        var raw = roomsResult.Value;
        var splitRaw = SplitMergedRooms(raw);

        var friendProfileIds = _gameLicenseService
            .ActiveCurrentFriends.Select(friend => FriendCodeGenerator.FriendCodeToProfileId(friend.FriendCode))
            .Where(profileId => profileId != 0)
            .ToHashSet();

        var rrRooms = splitRaw
            .Select(room => MapRoom(room, _whWzService, leaderboardByPid, leaderboardByFriendCode, friendProfileIds))
            .ToList();

        CurrentRooms = rrRooms;
    }

    private static RrRoom MapRoom(
        RwfcRoomStatusRoom room,
        IWhWzDataSingletonService whWzService,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByPid,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByFriendCode,
        IReadOnlySet<uint> friendProfileIds
    )
    {
        return new()
        {
            Id = room.Id,
            Created = room.Created,
            Type = room.Type,
            Suspend = room.Suspend,
            Rk = room.Rk,
            Players = room
                .Players.Select(p => MapPlayer(p, whWzService, leaderboardByPid, leaderboardByFriendCode, friendProfileIds))
                .ToList(),
        };
    }

    private static RrPlayer MapPlayer(
        RwfcRoomStatusPlayer p,
        IWhWzDataSingletonService whWzService,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByPid,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByFriendCode,
        IReadOnlySet<uint> friendProfileIds
    )
    {
        Mii? mii = null;
        if (p.Mii is not null && !string.IsNullOrWhiteSpace(p.Mii.Data))
        {
            try
            {
                var bytes = Convert.FromBase64String(p.Mii.Data);
                var des = MiiSerializer.Deserialize(bytes);
                if (des.IsSuccess)
                    mii = des.Value;
            }
            catch
            {
                // ignore invalid base64/serialization
            }
        }

        var friendCode = p.FriendCode ?? string.Empty;
        var profileId = FriendCodeGenerator.FriendCodeToProfileId(friendCode);

        var leaderboardEntry = GetLeaderboardEntry(p, friendCode, leaderboardByPid, leaderboardByFriendCode);

        return new()
        {
            Pid = p.Pid,
            Name = p.Name ?? string.Empty,
            FriendCode = friendCode,
            Vr = p.Vr,
            Br = p.Br,
            IsOpenHost = p.IsOpenHost,
            IsSuspended = p.IsSuspended,
            ConnectionMap = p.ConnectionMap ?? [],
            Mii = mii,
            BadgeVariants = whWzService.GetBadges(friendCode),
            LeaderboardRank = leaderboardEntry?.Rank ?? leaderboardEntry?.ActiveRank,
            IsFriend = profileId != 0 && friendProfileIds.Contains(profileId),
        };
    }

    private static RwfcLeaderboardEntry? GetLeaderboardEntry(
        RwfcRoomStatusPlayer player,
        string friendCode,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByPid,
        IReadOnlyDictionary<string, RwfcLeaderboardEntry> leaderboardByFriendCode
    )
    {
        if (leaderboardByPid.TryGetValue(player.Pid, out var byPid))
            return byPid;

        if (!string.IsNullOrWhiteSpace(friendCode) && leaderboardByFriendCode.TryGetValue(friendCode, out var byFriendCode))
            return byFriendCode;

        return null;
    }

    private static List<RwfcRoomStatusRoom> SplitMergedRooms(List<RwfcRoomStatusRoom> rooms)
    {
        var output = new List<RwfcRoomStatusRoom>();

        foreach (var room in rooms)
        {
            var n = room.Players.Count;

            // build adjacency of “two‐way” connections
            var adj = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
            var anyEdgeAdded = false;

            for (var i = 0; i < n; i++)
            {
                var mapList = room.Players[i].ConnectionMap;
                if (mapList is null || mapList.Count == 0)
                    continue;

                // /api/roomstatus: connectionMap is usually length n (including self), but can vary.
                var includesSelf = mapList.Count == n;
                var excludesSelf = mapList.Count == n - 1;

                if (!includesSelf && !excludesSelf)
                    continue;

                for (var j = 0; j < mapList.Count; j++)
                {
                    var entry = mapList[j];
                    var c = string.IsNullOrEmpty(entry) ? '0' : entry[0];
                    if (c == '0')
                        continue;

                    if (includesSelf && j == i)
                        continue;

                    var other = includesSelf ? j : (j >= i ? j + 1 : j);
                    if (other < 0 || other >= n)
                        continue;

                    // only add if we’ll later see the reverse link
                    adj[i].Add(other);
                    anyEdgeAdded = true;
                }
            }

            // If we have no connectivity information, don't attempt to split.
            if (!anyEdgeAdded)
            {
                output.Add(room);
                continue;
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
                    components.Select(comp => new RwfcRoomStatusRoom
                    {
                        Id = room.Id,
                        Type = room.Type,
                        Created = room.Created,
                        Host = room.Host,
                        Rk = room.Rk,
                        Suspend = room.Suspend,
                        Players = comp.Select(idx => room.Players[idx]).ToList(),
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
