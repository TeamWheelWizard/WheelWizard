using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;
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
    private static bool IsCompactModeEnabled;

    private ObservableCollection<FriendProfile> _friendlist = [];

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    [Inject]
    private IMiiDbService MiiDbService { get; set; } = null!;

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
        SetCompactMode();
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
        VisibleWhenNoFriends.IsVisible = FriendList.Count <= 0;
        VisibleWhenFriends.IsVisible = FriendList.Count > 0;
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

    private void ToggleCompactMode_OnClick(object? sender, RoutedEventArgs e)
    {
        IsCompactModeEnabled = !IsCompactModeEnabled;
        SetCompactMode();
    }

    private void SetCompactMode()
    {
        Control[] controlsToUpdate = [CompactModeButton, FriendsListView];
        foreach (var control in controlsToUpdate)
        {
            if (IsCompactModeEnabled)
                control.Classes.Add("Compact");
            else
                control.Classes.Remove("Compact");
        }
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
