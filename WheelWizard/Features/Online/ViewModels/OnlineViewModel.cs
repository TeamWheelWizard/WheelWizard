using System.ComponentModel;
using WheelWizard.Models.RRInfo;
using WheelWizard.Online.Domain;
using WheelWizard.RrRooms;
using WheelWizard.Shared.Polling;
using WheelWizard.Shared.Services;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Online.ViewModels;

public sealed class OnlineViewModel(LiveRoomsService rooms, IRrLeaderboardSingletonService leaderboard, IApiCaller<IRwfcApi> api)
    : INotifyPropertyChanged,
        IPollingListener
{
    private CancellationTokenSource? _search;
    public int PlayerCount => rooms.PlayerCount;
    public int RoomCount => rooms.CurrentRooms.Count(room => room.PlayerCount > 0);
    public IReadOnlyList<RrRoom> RoomPreview =>
        rooms.CurrentRooms.Where(room => room.PlayerCount > 0).OrderByDescending(room => room.PlayerCount).Take(2).ToList();
    public bool HasRooms => RoomCount > 0;
    public IReadOnlyList<OnlinePlayer> Results { get; private set; } = [];
    public bool IsSearching { get; private set; }
    public bool IsLoading { get; private set; }
    public bool IsEmpty => IsSearching && !IsLoading && Results.Count == 0;
    public string SearchMessage { get; private set; } = "";
    public string ResultCount => $"{Results.Count} players";

    public void Activate()
    {
        rooms.Subscribe(this);
        Notify();
    }

    public void Deactivate()
    {
        rooms.Unsubscribe(this);
        CancelSearch();
    }

    public void OnUpdate(ObservablePollingService sender) => Notify();

    public async Task SearchAsync(string? text)
    {
        CancelSearch();
        var query = text?.Trim() ?? "";
        IsSearching = query.Length > 0;
        IsLoading = IsSearching;
        Results = [];
        SearchMessage = "Online players and the top 50. Use a full friend code to find anyone.";
        Notify();
        if (!IsSearching)
            return;

        _search = new();
        var token = _search.Token;
        try
        {
            await Task.Delay(250, token);
            var ranked = await leaderboard.GetTopPlayersAsync(50).WaitAsync(token);
            token.ThrowIfCancellationRequested();
            var online = rooms.CurrentRooms.SelectMany(room => room.Players).DistinctBy(player => player.FriendCode).ToList();
            var matches = online
                .Where(player => Matches(player.Name, player.FriendCode, query))
                .Select(player => new OnlinePlayer(player.Name, player.FriendCode, player.VrDisplay, player.FirstMii, true))
                .ToList();
            if (ranked.IsSuccess)
                matches.AddRange(
                    ranked
                        .Value.Where(player => Matches(player.Name, player.FriendCode, query))
                        .Select(player => new OnlinePlayer(
                            player.Name,
                            player.FriendCode,
                            player.Vr?.ToString("N0") ?? "--",
                            ReadMii(player.MiiData),
                            online.Any(p => p.FriendCode == player.FriendCode)
                        ))
                );
            else
                SearchMessage = "Rankings are unavailable. Showing matching online players.";

            var digits = query.Replace("-", "").Replace(" ", "");
            if (digits.Length == 12 && digits.All(char.IsAsciiDigit))
            {
                var friendCode = $"{digits[..4]}-{digits[4..8]}-{digits[8..]}";
                if (matches.All(player => player.FriendCode != friendCode))
                {
                    var profile = await api.CallApiAsync(client => client.GetPlayerProfileAsync(friendCode)).WaitAsync(token);
                    if (profile.IsSuccess)
                    {
                        var player = profile.Value;
                        matches.Add(new(player.Name, player.FriendCode, player.Vr.ToString("N0"), ReadMii(player.MiiData), false));
                    }
                    else
                        SearchMessage = "Could not find that profile. Check the friend code and try again.";
                }
            }
            token.ThrowIfCancellationRequested();
            Results = matches.DistinctBy(player => player.FriendCode).ToList();
            IsLoading = false;
            Notify();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private static bool Matches(string? name, string friendCode, string query) =>
        (name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
        || friendCode.Replace("-", "").Contains(query.Replace("-", "").Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    private static Mii? ReadMii(string? data)
    {
        if (data is null || data.Length is < 90 or > 120)
            return null;
        var result = MiiSerializer.Deserialize(data);
        return result.IsSuccess ? result.Value : null;
    }

    private void CancelSearch()
    {
        _search?.Cancel();
        _search?.Dispose();
        _search = null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify() => PropertyChanged?.Invoke(this, new(null));
}
