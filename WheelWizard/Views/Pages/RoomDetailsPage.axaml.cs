using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.RRInfo;
using WheelWizard.Services.LiveData;
using WheelWizard.Utilities.Mockers;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;

namespace WheelWizard.Views.Pages;

public partial class RoomDetailsPage : UserControl, INotifyPropertyChanged, IRepeatedTaskListener
{
    private RrRoom _room;

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
        PlayersList = new(Room.Players.Values);
    }

    public RoomDetailsPage(RrRoom room)
    {
        InitializeComponent();
        DataContext = this;
        Room = room;

        PlayersList = new(Room.Players.Values);

        RrLiveRooms.Instance.Subscribe(this);
        Unloaded += RoomsDetailPage_Unloaded;
    }

    public void OnUpdate(RepeatedTaskManager sender)
    {
        if (sender is not RrLiveRooms liveRooms) return;

        var room = liveRooms.CurrentRooms.Find(r => r.Id == Room.Id);

        if (room == null)
        {
            // Reason we do this incase room gets disbanded or something idk
            ViewUtils.NavigateToPage(new RoomsPage());
            return;
        }

        Room = room;
        PlayersList.Clear();
        foreach (var p in room.Players.Values)
        {
            PlayersList.Add(p);
        }
    }

    private void GoBackClick(object? sender, EventArgs eventArgs) => ViewUtils.NavigateToPage(new RoomsPage());

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer) return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.Fc);
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer) return;
        if (selectedPlayer.FirstMii == null) return;
        new MiiCarouselWindow().SetMii(selectedPlayer.FirstMii).Show();
    }

    private void RoomsDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RrLiveRooms.Instance.Unsubscribe(this);
    }

    private void PlayerView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox listBox) return;
        listBox.ContextMenu?.Open();
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
