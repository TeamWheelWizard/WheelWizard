using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WheelWizard.Models;
using WheelWizard.RrRooms;
using WheelWizard.Services.LiveData;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.Services;
using WheelWizard.Utilities.Generators;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public sealed record PlayerSearchResultItem
{
    public required string Pid { get; init; }
    public required string Name { get; init; }
    public required string FriendCode { get; init; }
    public required string VrDisplay { get; init; }
    public required string WeekChangeText { get; init; }
    public required int Rank { get; init; }
    public Mii? Mii { get; init; }
    public bool IsOnline { get; init; }
    public bool IsSuspicious { get; init; }
    public BadgeVariant PrimaryBadge { get; init; }
    public bool HasBadge { get; init; }
    public bool HasRank => Rank > 0;
    public string RankText => HasRank ? $"#{Rank}" : string.Empty;
}

public partial class PlayerSearchPage : UserControlBase, INotifyPropertyChanged
{
    private const int PageSize = 25;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _debounceCts;
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalCount;

    [Inject]
    private IApiCaller<IRwfcApi> ApiCaller { get; set; } = null!;

    [Inject]
    private IWhWzDataSingletonService BadgeService { get; set; } = null!;

    [Inject]
    private IGameLicenseSingletonService GameDataService { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsManager { get; set; } = null!;

    public ObservableCollection<PlayerSearchResultItem> Players { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (_isLoading == value)
                return;

            _isLoading = value;
            OnPropertyChanged(nameof(IsLoading));
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (_hasError == value)
                return;

            _hasError = value;
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
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

    public bool HasResults => !IsLoading && !HasError && Players.Count > 0;
    public bool HasNoResults => !IsLoading && !HasError && Players.Count == 0;
    public bool CanGoPrevious => _currentPage > 1 && !IsLoading;
    public bool CanGoNext => _currentPage < _totalPages && !IsLoading;
    public string ResultCountText => _totalCount == 0 ? "No matches" : $"{_totalCount:N0} players";
    public string PageText => _totalCount == 0 ? "--" : $"{_currentPage:N0} / {_totalPages:N0}";

    public PlayerSearchPage()
    {
        InitializeComponent();
        DataContext = this;
        Players.CollectionChanged += Players_OnCollectionChanged;
        Loaded += PlayerSearchPage_Loaded;
        Unloaded += PlayerSearchPage_Unloaded;
    }

    private async void PlayerSearchPage_Loaded(object? sender, RoutedEventArgs e)
    {
        await ReloadPlayersAsync();
    }

    private void PlayerSearchPage_Unloaded(object? sender, RoutedEventArgs e)
    {
        CancelLoads();
    }

    private void Players_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void SearchTextBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _currentPage = 1;
        DebounceReload();
    }

    private void SearchButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _currentPage = 1;
        _ = ReloadPlayersAsync();
    }

    private async void RetryButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await ReloadPlayersAsync();
    }

    private async void PreviousPage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage <= 1)
            return;

        _currentPage--;
        await ReloadPlayersAsync();
    }

    private async void NextPage_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_currentPage >= _totalPages)
            return;

        _currentPage++;
        await ReloadPlayersAsync();
    }

    private void DebounceReload()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new();
        var token = _debounceCts.Token;

        _ = Task.Run(
            async () =>
            {
                try
                {
                    await Task.Delay(350, token);
                    await Dispatcher.UIThread.InvokeAsync(async () => await ReloadPlayersAsync());
                }
                catch (OperationCanceledException) { }
            },
            token
        );
    }

    private async Task ReloadPlayersAsync()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new();
        var cancellationToken = _loadCts.Token;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        var request = new RwfcLeaderboardRequest
        {
            Page = _currentPage,
            PageSize = PageSize,
            Search = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? null : SearchTextBox.Text.Trim(),
            SortBy = "rank",
            Ascending = true,
        };

        var result = await ApiCaller.CallApiAsync(api => api.GetLeaderboardAsync(request));
        if (cancellationToken.IsCancellationRequested)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (result.IsFailure || result.Value == null)
            {
                Players.Clear();
                _totalCount = 0;
                _totalPages = 1;
                ErrorMessage = result.Error?.Message ?? "Unable to search players.";
                HasError = true;
                IsLoading = false;
                NotifyPageMetaChanged();
                return;
            }

            ApplySearchResults(result.Value);
            IsLoading = false;
        });
    }

    private void ApplySearchResults(RwfcLeaderboardResponse response)
    {
        Players.Clear();
        _currentPage = Math.Max(1, response.CurrentPage);
        _totalPages = Math.Max(1, response.TotalPages);
        _totalCount = Math.Max(0, response.TotalCount);

        var onlineProfileIds = RRLiveRooms
            .Instance.CurrentRooms.SelectMany(room => room.Players)
            .Select(player => FriendCodeGenerator.FriendCodeToProfileId(player.FriendCode))
            .Where(profileId => profileId != 0)
            .ToHashSet();

        foreach (var entry in response.Players)
        {
            var friendCode = entry.FriendCode ?? string.Empty;
            var profileId = FriendCodeGenerator.FriendCodeToProfileId(friendCode);
            var badges = string.IsNullOrWhiteSpace(friendCode) ? [] : BadgeService.GetBadges(friendCode);
            var primaryBadge = badges.FirstOrDefault(BadgeVariant.None);

            Players.Add(
                new()
                {
                    Pid = entry.Pid,
                    Name = string.IsNullOrWhiteSpace(entry.Name) ? "Unknown Player" : entry.Name,
                    FriendCode = friendCode,
                    VrDisplay = entry.Vr?.ToString("N0") ?? "--",
                    WeekChangeText = FormatSignedValue(entry.VrStats?.LastWeek ?? 0),
                    Rank = ResolveRank(entry),
                    Mii = DeserializeMii(entry.MiiData),
                    IsOnline = profileId != 0 && onlineProfileIds.Contains(profileId),
                    IsSuspicious = entry.IsSuspicious,
                    PrimaryBadge = primaryBadge,
                    HasBadge = primaryBadge != BadgeVariant.None,
                }
            );
        }

        NotifyPageMetaChanged();
    }

    private static int ResolveRank(RwfcLeaderboardEntry entry)
    {
        if (entry.Rank is > 0 and <= 50000)
            return entry.Rank.Value;

        if (entry.ActiveRank is > 0 and <= 50000)
            return entry.ActiveRank.Value;

        return 0;
    }

    private static Mii? DeserializeMii(string? miiData)
    {
        if (string.IsNullOrWhiteSpace(miiData))
            return null;

        if (miiData.Length is < 90 or > 120)
            return null;

        var buffer = new byte[MiiSerializer.MiiBlockSize];
        if (!Convert.TryFromBase64String(miiData, buffer, out var bytesWritten) || bytesWritten != MiiSerializer.MiiBlockSize)
            return null;

        var result = MiiSerializer.Deserialize(buffer);
        return result.IsSuccess ? result.Value : null;
    }

    private void PlayerItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        if (e.Source is Button || e.Source is Control source && source.FindAncestorOfType<Button>() != null)
            return;

        if (sender == null || GetContextPlayer(sender) is not { FriendCode: { Length: > 0 } friendCode })
            return;

        new PlayerProfileWindow(friendCode).Show();
    }

    private void ViewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetContextPlayer(sender) is not { FriendCode: { Length: > 0 } friendCode })
            return;

        new PlayerProfileWindow(friendCode).Show();
    }

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player == null || string.IsNullOrWhiteSpace(player.FriendCode))
            return;

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(player.FriendCode);
        ViewUtils.ShowSnackbar(t("snackbar_success.copied_fc"));
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player?.Mii == null)
            return;

        new MiiCarouselWindow().SetMii(player.Mii).Show();
    }

    private async void AddFriend_OnClick(object sender, RoutedEventArgs e)
    {
        var player = GetContextPlayer(sender);
        if (player == null)
            return;

        if (player.Mii == null)
        {
            ViewUtils.ShowSnackbar("This player has no valid Mii data.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var focusedUserIndex = SettingsManager.Get<int>(SettingsManager.FOCUSED_USER);
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
            Pid = player.Pid,
            Name = player.Name,
            FriendCode = normalizedFriendCode,
            Vr = int.TryParse(player.VrDisplay.Replace(",", string.Empty), out var vr) ? vr : 0,
        };

        var shouldAdd = await new AddFriendConfirmationWindow(profile, player.Mii).AwaitAnswer();
        if (!shouldAdd)
            return;

        var addResult = GameDataService.AddFriend(focusedUserIndex, normalizedFriendCode, player.Mii, (uint)Math.Max(profile.Vr, 0));
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

    private static PlayerSearchResultItem? GetContextPlayer(object sender)
    {
        if (sender is Control { DataContext: PlayerSearchResultItem directPlayer })
            return directPlayer;

        if (sender is MenuItem menuItem && menuItem.DataContext is PlayerSearchResultItem menuPlayer)
            return menuPlayer;

        return null;
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

    private static string FormatSignedValue(int value)
    {
        if (value > 0)
            return $"+{value:N0}";

        return value.ToString("N0");
    }

    private void NotifyPageMetaChanged()
    {
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private void CancelLoads()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;

        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
