using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.GameData;
using WheelWizard.Services.LiveData;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WiiManagement;

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
                ListOrderCondition.VR => "Vr",
                ListOrderCondition.BR => "Br",
                ListOrderCondition.NAME => "Name",
                ListOrderCondition.WINS => "Total Wins",
                ListOrderCondition.TOTAL_RACES => "Total Races",
                ListOrderCondition.IS_ONLINE => "Is Online",
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
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        if (FriendsListView.SelectedItem is not FriendProfile selectedPlayer)
            return;
        if (selectedPlayer.Mii == null)
            return;
        new MiiCarouselWindow().SetMii(selectedPlayer.Mii).Show();
    }

    private void ViewRoom_OnClick(string friendCode)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.Value.Fc != friendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        new MessageBoxWindow()
            .SetTitleText("Couldn't find the room")
            .SetInfoText("Whoops, could not find the room that this player is supposedly playing in")
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .Show();
    }

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
