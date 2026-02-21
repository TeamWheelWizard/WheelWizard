using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models;
using WheelWizard.Resources.Languages;
using WheelWizard.RrRooms;
using WheelWizard.Services.LiveData;
using WheelWizard.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Shared.Services;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Views.Pages;

public partial class FriendsPage : UserControlBase, INotifyPropertyChanged, IRepeatedTaskListener
{
    // Made this static intentionally.
    // I personally don't think its worth saving it as a setting.
    // Though I do see the use in saving it when using the app so you can swap pages in the meantime
    private static ListOrderCondition CurrentOrder = ListOrderCondition.IS_ONLINE;

    private ObservableCollection<FriendProfile> _friendlist = [];

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    [Inject]
    private IMiiDbService MiiDbService { get; set; } = null!;

    [Inject]
    private IApiCaller<IRwfcApi> ApiCaller { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    public ObservableCollection<FriendProfile> FriendList
    {
        get => _friendlist;
        set
        {
            _friendlist = value;
            OnPropertyChanged(nameof(FriendList));
        }
    }

    public FriendsPage()
    {
        InitializeComponent();
        GameLicenseService.Subscribe(this);
        UpdateFriendList();

        DataContext = this;
        FriendsListView.ItemsSource = FriendList;
        PopulateSortingList();
        HandleVisibility();
    }

    public void OnUpdate(RepeatedTaskManager sender)
    {
        if (sender is not GameLicenseSingletonService)
            return;
        UpdateFriendList();
    }

    private void UpdateFriendList()
    {
        var newList = GetSortedPlayerList();
        // Instead of setting entire list every single time, we just update the indexes accordingly, which is faster
        for (var i = 0; i < newList.Count; i++)
        {
            if (i < FriendList.Count)
                FriendList[i] = newList[i];
            else
                FriendList.Add(newList[i]);
        }

        while (FriendList.Count > newList.Count)
        {
            FriendList.RemoveAt(FriendList.Count - 1);
        }

        ListItemCount.Text = FriendList.Count.ToString();
        HandleVisibility();
    }

    private void HandleVisibility()
    {
        var hasFriends = FriendList.Count > 0;
        VisibleWhenNoFriends.IsVisible = !hasFriends;
        VisibleWhenFriends.IsVisible = hasFriends;
        TopAddFriendButton.IsVisible = hasFriends;
    }

    private List<FriendProfile> GetSortedPlayerList()
    {
        Func<FriendProfile, object> orderMethod = CurrentOrder switch
        {
            ListOrderCondition.VR => f => f.Vr,
            ListOrderCondition.BR => f => f.Br,
            ListOrderCondition.NAME => f => f.NameOfMii,
            ListOrderCondition.WINS => f => f.Wins,
            ListOrderCondition.TOTAL_RACES => f => f.Losses + f.Wins,
            ListOrderCondition.IS_ONLINE or _ => f => f.IsOnline,
        };
        return GameLicenseService.ActiveCurrentFriends.OrderByDescending(orderMethod).ToList();
    }

    private void PopulateSortingList()
    {
        foreach (ListOrderCondition type in Enum.GetValues(typeof(ListOrderCondition)))
        {
            var name = type switch
            {
                // TODO: Should be replaced with actual translations
                ListOrderCondition.VR => Common.Attribute_VrFull,
                ListOrderCondition.BR => Common.Attribute_BrFull,
                ListOrderCondition.NAME => Common.Attribute_Name,
                ListOrderCondition.WINS => Common.Attribute_Wins,
                ListOrderCondition.TOTAL_RACES => Common.Attribute_RacesPlayed,
                ListOrderCondition.IS_ONLINE => Common.Attribute_IsOnline,
            };

            SortByDropdown.Items.Add(name);
        }

        SortByDropdown.SelectedIndex = (int)CurrentOrder;
    }

    private void SortByDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        CurrentOrder = (ListOrderCondition)SortByDropdown.SelectedIndex;
        UpdateFriendList();
    }

    private async void AddFriend_OnClick(object? sender, RoutedEventArgs e)
    {
        var focusedUserIndex = SettingsService.Get<int>(SettingsService.FOCUSED_USER);
        if (focusedUserIndex is < 0 or > 3)
        {
            ViewUtils.ShowSnackbar("Invalid license selected.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var activeUserPid = FriendCodeGenerator.FriendCodeToProfileId(GameLicenseService.ActiveUser.FriendCode);
        if (activeUserPid == 0)
        {
            ViewUtils.ShowSnackbar("Select a valid license before adding friends.", ViewUtils.SnackbarType.Warning);
            return;
        }

        if (GameLicenseService.ActiveCurrentFriends.Count >= 30)
        {
            ViewUtils.ShowSnackbar("Your friend list is full.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var inputFriendCode = await new TextInputWindow()
            .SetMainText("Add Friend")
            .SetExtraText("Enter a 12-digit friend code.")
            .SetPlaceholderText("0000-0000-0000")
            .SetButtonText(Common.Action_Cancel, Common.Action_Submit)
            .SetValidation((_, newText) => ValidateFriendCodeInput(newText))
            .ShowDialog();

        if (inputFriendCode == null)
            return;

        var normalizedFriendCodeResult = NormalizeFriendCode(inputFriendCode);
        if (normalizedFriendCodeResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(normalizedFriendCodeResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        var normalizedFriendCode = normalizedFriendCodeResult.Value;
        var profileResult = await ApiCaller.CallApiAsync(rwfcApi => rwfcApi.GetPlayerProfileAsync(normalizedFriendCode));
        if (profileResult.IsFailure || profileResult.Value == null)
        {
            ViewUtils.ShowSnackbar("Could not find that friend code online.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var miiResult = MiiSerializer.Deserialize(profileResult.Value.MiiData);
        if (miiResult.IsFailure)
        {
            ViewUtils.ShowSnackbar("This profile has no valid Mii data.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var profile = WithFallbackFriendCode(profileResult.Value, normalizedFriendCode);
        var shouldAdd = await new AddFriendConfirmationWindow(profile, miiResult.Value).AwaitAnswer();
        if (!shouldAdd)
            return;

        var addResult = GameLicenseService.AddFriend(
            focusedUserIndex,
            normalizedFriendCode,
            miiResult.Value,
            (uint)Math.Max(profile.Vr, 0)
        );

        if (addResult.IsFailure)
        {
            ViewUtils.ShowSnackbar(addResult.Error.Message, ViewUtils.SnackbarType.Warning);
            return;
        }

        UpdateFriendList();
        ViewUtils.GetLayout().UpdateFriendCount();
        ViewUtils.ShowSnackbar($"Added {profile.Name} to your friend list.");
    }

    private OperationResult ValidateFriendCodeInput(string? rawFriendCode)
    {
        var normalizedFriendCodeResult = NormalizeFriendCode(rawFriendCode ?? string.Empty);
        if (normalizedFriendCodeResult.IsFailure)
            return normalizedFriendCodeResult.Error;

        var friendProfileId = FriendCodeGenerator.FriendCodeToProfileId(normalizedFriendCodeResult.Value);
        var currentProfileId = FriendCodeGenerator.FriendCodeToProfileId(GameLicenseService.ActiveUser.FriendCode);
        if (currentProfileId != 0 && currentProfileId == friendProfileId)
            return Fail("You cannot add your own friend code.");

        var duplicateFriend = GameLicenseService.ActiveCurrentFriends.Any(friend =>
        {
            var existingPid = FriendCodeGenerator.FriendCodeToProfileId(friend.FriendCode);
            return existingPid != 0 && existingPid == friendProfileId;
        });

        return duplicateFriend ? Fail("This friend is already in your list.") : Ok();
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

    private static PlayerProfileResponse WithFallbackFriendCode(PlayerProfileResponse profile, string fallbackFriendCode)
    {
        if (!string.IsNullOrWhiteSpace(profile.FriendCode))
            return profile;

        return new()
        {
            Pid = profile.Pid,
            Name = profile.Name,
            FriendCode = fallbackFriendCode,
            Vr = profile.Vr,
            Rank = profile.Rank,
            LastSeen = profile.LastSeen,
            IsSuspicious = profile.IsSuspicious,
            VrStats = profile.VrStats,
            MiiData = profile.MiiData,
        };
    }

    private enum ListOrderCondition
    {
        IS_ONLINE,
        VR,
        BR,
        NAME,
        WINS,
        TOTAL_RACES,
    }

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.FriendCode);
        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_CopiedFC);
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        if (selectedPlayer.Mii == null)
            return;
        new MiiCarouselWindow().SetMii(selectedPlayer.Mii).Show();
    }

    private void ViewProfile_OnClick(object sender, RoutedEventArgs e)
    {
        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        if (string.IsNullOrEmpty(selectedPlayer.FriendCode))
            return;
        new PlayerProfileWindow(selectedPlayer.FriendCode).Show();
    }

    private void RemoveFriend_OnClick(object sender, RoutedEventArgs e)
    {
        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        if (string.IsNullOrWhiteSpace(selectedPlayer.FriendCode))
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

    private void ViewRoom_OnClick(string friendCode)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.FriendCode != friendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CouldNotFindRoom);
    }

    private void SaveMii_OnClick(object sender, RoutedEventArgs e)
    {
        if (!MiiDbService.Exists())
        {
            ViewUtils.ShowSnackbar(Phrases.SnackbarWarning_CantSaveMii, ViewUtils.SnackbarType.Warning);
            return;
        }

        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        if (selectedPlayer.Mii == null)
            return;

        var desiredMii = selectedPlayer.Mii;

        //We set the miiId to 0 so it will be added as a new Mii
        desiredMii.MiiId = 0;
        //Since we are actually copying this mii, we want to set the mac Adress to a dummy value
        var macAddress = "02:11:11:11:11:11";
        var databaseResult = MiiDbService.AddToDatabase(desiredMii, macAddress);
        if (databaseResult.IsFailure)
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Error_FailedCopyMii, null, [databaseResult.Error!.Message]);
            return;
        }

        ViewUtils.ShowSnackbar(Phrases.SnackbarSuccess_MiiAdded);
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
