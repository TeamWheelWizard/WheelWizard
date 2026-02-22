using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models;
using WheelWizard.Resources.Languages;
using WheelWizard.RrRooms;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.Generators;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public partial class LeaderboardPage : UserControlBase, INotifyPropertyChanged
{
    private static readonly LeaderboardPlayerItem EmptyPodiumPlayer = new()
    {
        Rank = 0,
        PlacementLabel = string.Empty,
        Name = string.Empty,
        FriendCode = string.Empty,
        VrText = string.Empty,
        PrimaryBadge = BadgeVariant.None,
        HasBadge = false,
        IsSuspicious = false,
        IsEvenRow = true,
    };

    private CancellationTokenSource? _loadCts;

    [Inject]
    private IRrLeaderboardSingletonService LeaderboardService { get; set; } = null!;

    [Inject]
    private IWhWzDataSingletonService BadgeService { get; set; } = null!;

    [Inject]
    private IGameLicenseSingletonService GameDataService { get; set; } = null!;

    private bool _hasLoadedOnce;
    private bool _isLoading;
    private bool _hasError;
    private bool _hasNoData;
    private bool _hasData;
    private string _errorMessage = string.Empty;
    private int _loadedPlayerCount;
    private LeaderboardPlayerItem? _podiumFirst;
    private LeaderboardPlayerItem? _podiumSecond;
    private LeaderboardPlayerItem? _podiumThird;

    public ObservableCollection<LeaderboardPlayerItem> RemainingPlayers { get; } = [];

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
                return;
            _hasError = value;
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
                return;
            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
        }
    }

    public bool HasNoData
    {
        get => _hasNoData;
        private set
        {
            if (_hasNoData == value)
                return;
            _hasNoData = value;
            OnPropertyChanged(nameof(HasNoData));
        }
    }

    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (_hasData == value)
                return;
            _hasData = value;
            OnPropertyChanged(nameof(HasData));
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
                return;
            _errorMessage = value;
            OnPropertyChanged(nameof(ErrorMessage));
        }
    }

    public string TotalPlayerCountText => _loadedPlayerCount.ToString();

    public string RemainingCountText => $"{RemainingPlayers.Count} players";

    public LeaderboardPlayerItem? PodiumFirst
    {
        get => _podiumFirst ?? EmptyPodiumPlayer;
        private set => SetPodiumPlayer(ref _podiumFirst, value, nameof(PodiumFirst), nameof(HasPodiumFirst));
    }

    public LeaderboardPlayerItem? PodiumSecond
    {
        get => _podiumSecond ?? EmptyPodiumPlayer;
        private set => SetPodiumPlayer(ref _podiumSecond, value, nameof(PodiumSecond), nameof(HasPodiumSecond));
    }

    public LeaderboardPlayerItem? PodiumThird
    {
        get => _podiumThird ?? EmptyPodiumPlayer;
        private set => SetPodiumPlayer(ref _podiumThird, value, nameof(PodiumThird), nameof(HasPodiumThird));
    }

    public bool HasPodiumFirst => _podiumFirst != null;
    public bool HasPodiumSecond => _podiumSecond != null;
    public bool HasPodiumThird => _podiumThird != null;

    public LeaderboardPage()
    {
        InitializeComponent();
        DataContext = this;
        RemainingPlayers.CollectionChanged += RemainingPlayers_OnCollectionChanged;

        Loaded += LeaderboardPage_Loaded;
        Unloaded += LeaderboardPage_Unloaded;
    }

    private async void LeaderboardPage_Loaded(object? sender, RoutedEventArgs e)
    {
        if (_hasLoadedOnce)
            return;

        _hasLoadedOnce = true;
        await ReloadLeaderboardAsync();
    }

    private void LeaderboardPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        CancelCurrentLoad();
    }

    private void RemainingPlayers_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(RemainingCountText));
    }

    private async void RetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ReloadLeaderboardAsync();
    }

    private async Task ReloadLeaderboardAsync()
    {
        CancelCurrentLoad();
        _loadCts = new();
        var cancellationToken = _loadCts.Token;

        SetLoadingState();
        ClearLeaderboardData();
        await Task.Yield();

        var leaderboardResult = await LeaderboardService.GetTopPlayersAsync(50);
        if (cancellationToken.IsCancellationRequested)
            return;

        if (leaderboardResult.IsFailure)
        {
            SetErrorState(leaderboardResult.Error?.Message ?? "Unable to fetch leaderboard.");
            return;
        }

        var orderedEntries = leaderboardResult
            .Value.Select((entry, index) => new { Entry = entry, Rank = ResolveRank(entry, index) })
            .OrderBy(entry => entry.Rank)
            .Take(50)
            .ToList();

        var friendProfileIds = GameDataService
            .ActiveCurrentFriends.Select(friend => FriendCodeGenerator.FriendCodeToProfileId(friend.FriendCode))
            .Where(profileId => profileId != 0)
            .ToHashSet();
        var onlineProfileIds = RRLiveRooms
            .Instance.CurrentRooms.SelectMany(room => room.Players)
            .Select(player => FriendCodeGenerator.FriendCodeToProfileId(player.FriendCode))
            .Where(profileId => profileId != 0)
            .ToHashSet();

        if (orderedEntries.Count == 0)
        {
            SetEmptyState();
            return;
        }

        List<LeaderboardPlayerItem> mappedPlayers;
        try
        {
            mappedPlayers = await Task.Run(
                () =>
                    orderedEntries
                        .Select(
                            (entry, index) => CreateLeaderboardPlayer(entry.Entry, entry.Rank, index, friendProfileIds, onlineProfileIds)
                        )
                        .ToList(),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        _loadedPlayerCount = mappedPlayers.Count;
        OnPropertyChanged(nameof(TotalPlayerCountText));

        PodiumFirst = mappedPlayers.ElementAtOrDefault(0);
        PodiumSecond = mappedPlayers.ElementAtOrDefault(1);
        PodiumThird = mappedPlayers.ElementAtOrDefault(2);

        if (cancellationToken.IsCancellationRequested)
            return;

        foreach (var player in mappedPlayers.Skip(3))
        {
            RemainingPlayers.Add(player);
        }

        SetDataState();
    }

    private LeaderboardPlayerItem CreateLeaderboardPlayer(
        RwfcLeaderboardEntry entry,
        int rank,
        int index,
        IReadOnlySet<uint> friendProfileIds,
        IReadOnlySet<uint> onlineProfileIds
    )
    {
        var friendCode = entry.FriendCode ?? string.Empty;
        var profileId = FriendCodeGenerator.FriendCodeToProfileId(friendCode);
        var badges = string.IsNullOrWhiteSpace(friendCode) ? [] : BadgeService.GetBadges(friendCode);
        var primaryBadge = badges.FirstOrDefault(BadgeVariant.None);

        return new()
        {
            Rank = rank,
            PlacementLabel = GetPlacementLabel(rank),
            Name = string.IsNullOrWhiteSpace(entry.Name) ? "Unknown Player" : entry.Name,
            FriendCode = friendCode,
            VrText = entry.Vr?.ToString("N0") ?? "--",
            Mii = DeserializeMii(entry.MiiData),
            PrimaryBadge = primaryBadge,
            HasBadge = primaryBadge != BadgeVariant.None,
            IsSuspicious = entry.IsSuspicious,
            IsEvenRow = index % 2 == 0,
            IsFriend = profileId != 0 && friendProfileIds.Contains(profileId),
            IsOnline = profileId != 0 && onlineProfileIds.Contains(profileId),
        };
    }

    private static int ResolveRank(RwfcLeaderboardEntry entry, int index)
    {
        if (entry.Rank is > 0 and <= 50000)
            return entry.Rank.Value;

        if (entry.ActiveRank is > 0 and <= 50000)
            return entry.ActiveRank.Value;

        return index + 1;
    }

    private static string GetPlacementLabel(int rank) =>
        rank switch
        {
            1 => "Champion",
            2 => "2nd Place",
            3 => "3rd Place",
            _ => $"#{rank}",
        };

    private static Mii? DeserializeMii(string? miiData)
    {
        if (string.IsNullOrWhiteSpace(miiData))
            return null;

        // Mii block payload should be ~100 base64 chars (74 bytes decoded).
        // Guarding the size avoids expensive decode failures for large non-Mii payloads.
        if (miiData.Length is < 90 or > 120)
            return null;

        var buffer = new byte[MiiSerializer.MiiBlockSize];
        if (!Convert.TryFromBase64String(miiData, buffer, out var bytesWritten) || bytesWritten != MiiSerializer.MiiBlockSize)
            return null;

        var result = MiiSerializer.Deserialize(buffer);
        return result.IsSuccess ? result.Value : null;
    }

    private void SetPodiumPlayer(
        ref LeaderboardPlayerItem? field,
        LeaderboardPlayerItem? value,
        string propertyName,
        string hasPropertyName
    )
    {
        if (field == value)
            return;

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(hasPropertyName);
    }

    private void SetLoadingState()
    {
        IsLoading = true;
        HasError = false;
        HasNoData = false;
        HasData = false;
        ErrorMessage = string.Empty;
    }

    private void SetErrorState(string message)
    {
        IsLoading = false;
        HasError = true;
        HasNoData = false;
        HasData = false;
        ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Failed to load leaderboard." : message;
    }

    private void SetEmptyState()
    {
        IsLoading = false;
        HasError = false;
        HasNoData = true;
        HasData = false;
    }

    private void SetDataState()
    {
        IsLoading = false;
        HasError = false;
        HasNoData = false;
        HasData = true;
    }

    private void ClearLeaderboardData()
    {
        PodiumFirst = null;
        PodiumSecond = null;
        PodiumThird = null;
        RemainingPlayers.Clear();
        _loadedPlayerCount = 0;

        OnPropertyChanged(nameof(TotalPlayerCountText));
        OnPropertyChanged(nameof(RemainingCountText));
    }

    private void CancelCurrentLoad()
    {
        if (_loadCts == null)
            return;

        _loadCts.Cancel();
        _loadCts.Dispose();
        _loadCts = null;
    }

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player == null || string.IsNullOrWhiteSpace(player.FriendCode))
            return;

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(player.FriendCode);
        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_CopiedFC);
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player?.FirstMii == null)
            return;

        new MiiCarouselWindow().SetMii(player.FirstMii).Show();
    }

    private void ViewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player == null || string.IsNullOrWhiteSpace(player.FriendCode))
            return;

        new PlayerProfileWindow(player.FriendCode).Show();
    }

    private async void AddFriend_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player == null)
            return;

        if (player.FirstMii == null)
        {
            ViewUtils.ShowSnackbar("This player has no valid Mii data.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var focusedUserIndex = (int)SettingsManager.FOCUSSED_USER.Get();
        if (focusedUserIndex is < 0 or > 3)
        {
            ViewUtils.ShowSnackbar("Invalid license selected.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var activeUserPid = FriendCodeGenerator.FriendCodeToProfileId(GameDataService.ActiveUser.FriendCode);
        if (activeUserPid == 0)
        {
            ViewUtils.ShowSnackbar("Select a valid license before adding friends.", ViewUtils.SnackbarType.Warning);
            return;
        }

        if (GameDataService.ActiveCurrentFriends.Count >= 30)
        {
            ViewUtils.ShowSnackbar("Your friend list is full.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var normalizedFriendCodeResult = NormalizeFriendCode(player.FriendCode);
        if (normalizedFriendCodeResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(normalizedFriendCodeResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        var normalizedFriendCode = normalizedFriendCodeResult.Value;
        var friendProfileId = FriendCodeGenerator.FriendCodeToProfileId(normalizedFriendCode);
        if (activeUserPid == friendProfileId)
        {
            ViewUtils.ShowSnackbar("You cannot add your own friend code.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var duplicateFriend = GameDataService.ActiveCurrentFriends.Any(friend =>
        {
            var existingPid = FriendCodeGenerator.FriendCodeToProfileId(friend.FriendCode);
            return existingPid != 0 && existingPid == friendProfileId;
        });

        if (duplicateFriend)
        {
            ViewUtils.ShowSnackbar("This friend is already in your list.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var profile = new PlayerProfileResponse
        {
            Name = player.Name,
            FriendCode = normalizedFriendCode,
            Vr = int.TryParse(player.VrText.Replace(",", string.Empty), out var vr) ? vr : 0,
        };

        var shouldAdd = await new AddFriendConfirmationWindow(profile, player.FirstMii).AwaitAnswer();
        if (!shouldAdd)
            return;

        var addResult = GameDataService.AddFriend(focusedUserIndex, normalizedFriendCode, player.FirstMii, (uint)Math.Max(profile.Vr, 0));

        if (addResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(addResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        ViewUtils.GetLayout().UpdateFriendCount();
        ViewUtils.ShowSnackbar($"Added {player.Name} to your friend list.");
    }

    private void JoinRoom_OnClick(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return;

        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.FriendCode != friendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        ViewUtils.ShowSnackbar("Could not find an active room for this player.", ViewUtils.SnackbarType.Warning);
    }

    private static LeaderboardPlayerItem? GetContextPlayer(object sender)
    {
        if (sender is not Control control)
            return null;
        return control.DataContext as LeaderboardPlayerItem;
    }

    private static OperationResult<string> NormalizeFriendCode(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return Fail("Friend code cannot be empty.");

        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        if (digits.Length != 12 || !ulong.TryParse(digits, out _))
            return Fail("Friend code must be exactly 12 digits.");

        var formatted = $"{digits[..4]}-{digits.Substring(4, 4)}-{digits.Substring(8, 4)}";
        var profileId = FriendCodeGenerator.FriendCodeToProfileId(formatted);
        if (profileId == 0)
            return Fail("Invalid friend code.");

        return formatted;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
