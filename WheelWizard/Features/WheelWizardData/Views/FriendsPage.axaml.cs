using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.RrRooms;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Shared.Polling;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WheelWizardData.Views.Dialogs;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.WheelWizardData.Views;

public partial class FriendsPage : UserControl, INotifyPropertyChanged, IPollingListener
{
    private IPopupFactory Popups { get; }

    private INavigationService Navigation { get; }

    // Made this static intentionally.
    // I personally don't think its worth saving it as a setting.
    // Though I do see the use in saving it when using the app so you can swap pages in the meantime
    private static ListOrderCondition CurrentOrder = ListOrderCondition.IS_ONLINE;

    private ObservableCollection<FriendProfile> _friendlist = [];

    private LiveRoomsService LiveRooms { get; }

    private IGameLicenseSingletonService GameLicenseService { get; }

    private IMiiDbService MiiDbService { get; }

    private ISettingsManager SettingsService { get; }

    /// <summary>The friends to show, in order (filled in a few at a time when the page opens, see <see cref="ApplyFriendList"/>).</summary>
    public ObservableCollection<FriendProfile> FriendList
    {
        get => _friendlist;
        set
        {
            _friendlist = value;
            OnPropertyChanged(nameof(FriendList));
        }
    }

    private int _friendCount;
    private int _onlineCount;

    /// <summary>All friends, also the ones not in <see cref="FriendList"/> yet.</summary>
    public int FriendCount
    {
        get => _friendCount;
        private set
        {
            _friendCount = value;
            OnPropertyChanged(nameof(FriendCount));
        }
    }

    public int OnlineCount
    {
        get => _onlineCount;
        private set
        {
            _onlineCount = value;
            OnPropertyChanged(nameof(OnlineCount));
        }
    }

    /// <summary>Names of the ways to sort, in the order of <see cref="SortIndex"/>.</summary>
    public IReadOnlyList<string> SortOptions { get; } = Enum.GetValues<ListOrderCondition>().Select(SortName).ToList();

    /// <summary>The picked way to sort (an index into <see cref="SortOptions"/>); setting it sorts the list.</summary>
    public int SortIndex
    {
        get => (int)CurrentOrder;
        set
        {
            if (value < 0 || value == (int)CurrentOrder)
                return;
            CurrentOrder = (ListOrderCondition)value;
            OnPropertyChanged(nameof(SortIndex));
            SortButton.Text = SortOptions[value];
            UpdateFriendList();
        }
    }

    public FriendsPage(
        IPopupFactory popups,
        INavigationService navigation,
        LiveRoomsService liveRooms,
        IGameLicenseSingletonService gameLicenseService,
        IMiiDbService miiDbService,
        ISettingsManager settingsService
    )
    {
        Popups = popups;
        Navigation = navigation;
        LiveRooms = liveRooms;
        GameLicenseService = gameLicenseService;
        MiiDbService = miiDbService;
        SettingsService = settingsService;
        InitializeComponent();
        GameLicenseService.Subscribe(this);
        UpdateFriendList();

        DataContext = this;
        SortButton.Text = SortOptions[SortIndex];
        HandleVisibility();
    }

    public void OnUpdate(ObservablePollingService sender)
    {
        if (sender is not GameLicenseSingletonService)
            return;
        UpdateFriendList();
    }

    /// <summary>Cards built right away when the page opens (a screenful); the rest follow in the background.</summary>
    private const int CardsAtOnce = 6;

    /// <summary>The friends to show, in order. <see cref="FriendList"/> catches up with it card by card.</summary>
    private List<FriendProfile> _friends = [];

    private bool _addingCards;

    private void UpdateFriendList()
    {
        _friends = GetSortedPlayerList();
        ApplyFriendList(Math.Max(FriendList.Count, CardsAtOnce));
        FriendCount = _friends.Count;
        OnlineCount = _friends.Count(friend => friend.IsOnline);
        // The dot before "8 of 24 online" lights up while anyone is online.
        CountLine.Classes.Set("AnyOnline", OnlineCount > 0);
        HandleVisibility();
    }

    /// <summary>
    /// Shows the first <paramref name="count"/> friends, updating the list in place: a card (with its live Mii) is only
    /// built for a friend that's new to the list, not on every refresh or when friends just change places. A card
    /// takes a while to build, so the ones further down are added one at a time while the page is idle, instead of
    /// all of them holding up the page opening.
    /// </summary>
    private void ApplyFriendList(int count)
    {
        count = Math.Min(count, _friends.Count);
        for (var i = 0; i < count; i++)
        {
            var current = FriendList.IndexOf(_friends[i]);
            if (current == i)
                continue;
            if (current > i)
                FriendList.Move(current, i);
            else
                FriendList.Insert(i, _friends[i]);
        }

        while (FriendList.Count > count)
            FriendList.RemoveAt(FriendList.Count - 1);

        if (FriendList.Count >= _friends.Count || _addingCards)
            return;
        _addingCards = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _addingCards = false;
                ApplyFriendList(FriendList.Count + 1);
            },
            DispatcherPriority.Background
        );
    }

    private void HandleVisibility()
    {
        var hasFriends = _friends.Count > 0;
        VisibleWhenNoFriends.IsVisible = !hasFriends;
        VisibleWhenFriends.IsVisible = hasFriends;
    }

    private List<FriendProfile> GetSortedPlayerList()
    {
        Func<FriendProfile, object> orderMethod = CurrentOrder switch
        {
            ListOrderCondition.VR => f => f.Vr,
            ListOrderCondition.NAME => f => f.NameOfMii,
            ListOrderCondition.WINS => f => f.Wins,
            ListOrderCondition.TOTAL_RACES => f => f.Losses + f.Wins,
            ListOrderCondition.IS_ONLINE or _ => f => f.IsOnline,
        };
        return GameLicenseService.ActiveCurrentFriends.OrderByDescending(orderMethod).ToList();
    }

    private static string SortName(ListOrderCondition type) =>
        type switch
        {
            ListOrderCondition.VR => t("attribute.vr_full"),
            ListOrderCondition.NAME => t("attribute.name"),
            ListOrderCondition.WINS => t("attribute.wins"),
            ListOrderCondition.TOTAL_RACES => t("attribute.races_played"),
            ListOrderCondition.IS_ONLINE => t("attribute.is_online"),
            _ => t("state.unknown"),
        };

    private enum ListOrderCondition
    {
        IS_ONLINE,
        VR,
        NAME,
        WINS,
        TOTAL_RACES,
    }

    #region Sorting

    // Filled here rather than in the menu's Opening event: that one only fires for a right-click, not for Open().
    private void SortButton_OnClick(object? sender, RoutedEventArgs e)
    {
        FillSortMenu();
        SortMenu.Open(SortButton);
    }

    private void SortMenu_OnOpening(object? sender, CancelEventArgs e) => FillSortMenu();

    /// <summary>Lists the ways to sort, the current one marked by colour and a check.</summary>
    private void FillSortMenu()
    {
        var accent = this.FindResource("Primary300") is Color color ? new SolidColorBrush(color) : null;
        SortMenu.Items.Clear();
        for (var i = 0; i < SortOptions.Count; i++)
        {
            var index = i;
            var item = new MenuItem { Header = SortOptions[index] };
            if (index == SortIndex)
            {
                item.FontWeight = FontWeight.SemiBold;
                item.Foreground = accent;
                item.Icon = new PathIcon
                {
                    Data = this.FindResource("CheckMark") as Geometry,
                    Width = 10,
                    Height = 10,
                    Foreground = accent,
                };
            }
            item.Click += (_, _) => SortIndex = index;
            SortMenu.Items.Add(item);
        }
    }

    #endregion

    #region Friend actions

    private static FriendProfile? FriendOf(object? sender) => (sender as StyledElement)?.DataContext as FriendProfile;

    /// <summary>A left click on a card opens the same menu a right-click does (its buttons handle their own clicks).</summary>
    private void Card_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control card || e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (!new Rect(card.Bounds.Size).Contains(e.GetPosition(card)))
            return;
        card.ContextMenu?.Open(card);
        e.Handled = true;
    }

    private void ViewRoom_OnClick(object? sender, RoutedEventArgs e)
    {
        if (FriendOf(sender) is { } friend)
            ViewRoom(friend.FriendCode);
    }

    private void CopyFriendCode_OnClick(object? sender, RoutedEventArgs e) => CopyFriendCode(FriendOf(sender));

    private void ViewProfile_OnClick(object? sender, RoutedEventArgs e) => ViewProfile(FriendOf(sender));

    private void ViewOnRwfc_OnClick(object? sender, RoutedEventArgs e) => ViewOnRwfc(FriendOf(sender));

    private void RemoveFriend_OnClick(object? sender, RoutedEventArgs e) => RemoveFriend(FriendOf(sender));

    private void CopyFriendCode(FriendProfile? selectedPlayer)
    {
        if (selectedPlayer is null)
            return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.FriendCode);
        ViewUtils.ShowSnackbar(t("snackbar_success.copied_fc"));
    }

    private void ViewProfile(FriendProfile? selectedPlayer)
    {
        if (string.IsNullOrEmpty(selectedPlayer?.FriendCode))
            return;
        Popups.Create<PlayerProfileWindow>(selectedPlayer.FriendCode).Show();
    }

    private void ViewOnRwfc(FriendProfile? selectedPlayer)
    {
        if (selectedPlayer is not null)
            ViewUtils.OpenRwfcPlayer(selectedPlayer.FriendCode);
    }

    private void RemoveFriend(FriendProfile? selectedPlayer)
    {
        if (selectedPlayer is null || string.IsNullOrWhiteSpace(selectedPlayer.FriendCode))
            return;

        var focusedUserIndex = SettingsService.Get<int>(SettingsService.FOCUSED_USER);
        if (focusedUserIndex is < 0 or > 3)
        {
            ViewUtils.ShowSnackbar("Invalid license selected.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var removeResult = GameLicenseService.RemoveFriend(focusedUserIndex, selectedPlayer.FriendCode);
        if (removeResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(removeResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        UpdateFriendList();
        ViewUtils.GetLayout().UpdateFriendCount();
        ViewUtils.ShowSnackbar($"Removed {selectedPlayer.NameOfMii} from your friend list.");
    }

    private void ViewRoom(string friendCode)
    {
        foreach (var room in LiveRooms.CurrentRooms)
        {
            if (room.Players.All(player => player.FriendCode != friendCode))
                continue;

            Navigation.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CouldNotFindRoom);
    }

    #endregion

    /// <summary>Copies the friend's Mii into "My Miis" (not offered in the menu).</summary>
    private void SaveMii(FriendProfile? selectedPlayer)
    {
        if (!MiiDbService.Exists())
        {
            ViewUtils.ShowSnackbar(t("snackbar_warning.cant_save_mii"), ViewUtils.SnackbarType.Warning);
            return;
        }

        if (selectedPlayer?.Mii == null)
            return;

        var desiredMii = selectedPlayer.Mii;

        //We set the miiId to 0 so it will be added as a new Mii
        desiredMii.MiiId = 0;
        //Since we are actually copying this mii, we want to set the mac Adress to a dummy value
        var macAddress = "02:11:11:11:11:11";
        var databaseResult = MiiDbService.AddToDatabase(desiredMii, macAddress);
        if (databaseResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(
                MessageTranslation.Error_FailedCopyMii,
                null,
                new { error = databaseResult.Error!.Message }
            );
            return;
        }

        ViewUtils.ShowSnackbar(t("snackbar_success.mii_added"));
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
