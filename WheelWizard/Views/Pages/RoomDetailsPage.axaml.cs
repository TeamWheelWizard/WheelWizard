using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models;
using WheelWizard.Models.RRInfo;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.Mockers;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Views.Pages;

public partial class RoomDetailsPage : UserControlBase, INotifyPropertyChanged, IRepeatedTaskListener
{
    [Inject]
    private IGameLicenseSingletonService GameDataService { get; set; } = null!;

    [Inject]
    private IMiiDbService MiiDbService { get; set; } = null!;

    private RrRoom _room = null!;

    public RrRoom Room
    {
        get => _room;
        set
        {
            _room = value;
            OnPropertyChanged(nameof(Room));
        }
    }

    private readonly ObservableCollection<RrPlayer> _playersList = [];

    public ObservableCollection<RrPlayer> PlayersList
    {
        get => _playersList;
        init
        {
            _playersList = value;
            OnPropertyChanged(nameof(PlayersList));
        }
    }

    public RoomDetailsPage()
    {
        InitializeComponent();
        DataContext = this;
        Room = RrRoomFactory.Instance.Create(); // Create a fake room for design-time preview
        PlayersList = new(Room.Players);
    }

    public RoomDetailsPage(RrRoom room)
    {
        InitializeComponent();
        DataContext = this;
        Room = room;

        PlayersList = new(Room.Players);

        RRLiveRooms.Instance.Subscribe(this);
        Unloaded += RoomsDetailPage_Unloaded;
    }

    public void OnUpdate(RepeatedTaskManager sender)
    {
        if (sender is not RRLiveRooms liveRooms)
            return;

        var room = liveRooms.CurrentRooms.Find(r => r.Id == Room.Id);

        if (room == null)
        {
            // Reason we do this incase room gets disbanded or something idk
            NavigationManager.NavigateTo<RoomsPage>();
            return;
        }

        Room = room;
        PlayersList.Clear();
        foreach (var p in room.Players)
        {
            PlayersList.Add(p);
        }
    }

    private void GoBackClick(object? sender, EventArgs eventArgs) => NavigationManager.NavigateTo<RoomsPage>();

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.FriendCode);
        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_CopiedFC);
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        if (selectedPlayer.FirstMii == null)
            return;
        new MiiCarouselWindow().SetMii(selectedPlayer.FirstMii).Show();
    }

    private void ViewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        if (string.IsNullOrEmpty(selectedPlayer.FriendCode))
            return;
        new PlayerProfileWindow(selectedPlayer.FriendCode).Show();
    }

    private async void AddFriend_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;

        if (selectedPlayer.FirstMii == null)
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

        var normalizedFriendCodeResult = NormalizeFriendCode(selectedPlayer.FriendCode);
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
            Name = selectedPlayer.Name,
            FriendCode = normalizedFriendCode,
            Vr = Math.Max(selectedPlayer.Vr ?? 0, 0),
        };

        var shouldAdd = await new AddFriendConfirmationWindow(profile, selectedPlayer.FirstMii).AwaitAnswer();
        if (!shouldAdd)
            return;

        var addResult = GameDataService.AddFriend(
            focusedUserIndex,
            normalizedFriendCode,
            selectedPlayer.FirstMii,
            (uint)Math.Max(selectedPlayer.Vr ?? 0, 0)
        );

        if (addResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(addResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        ViewUtils.GetLayout().UpdateFriendCount();
        ViewUtils.ShowSnackbar($"Added {selectedPlayer.Name} to your friend list.");
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

    private void RoomsDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RRLiveRooms.Instance.Unsubscribe(this);
    }

    private void PlayerView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox listBox)
            return;
        listBox.ContextMenu?.Open();
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
