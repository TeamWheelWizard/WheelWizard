using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    
    private readonly ObservableCollection<RrPlayer> _playersList = new();
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
        PlayersList = new ObservableCollection<RrPlayer>(Room.Players.Values);
        ListItemCount.Text = PlayersList.Count.ToString();
    }

    public RoomDetailsPage(RrRoom room)
    {
        InitializeComponent();
        DataContext = this;
        Room = room;
        
        PlayersList = new ObservableCollection<RrPlayer>(Room.Players.Values);

        RRLiveRooms.Instance.Subscribe(this);
        ListItemCount.Text = PlayersList.Count.ToString();
        Unloaded += RoomsDetailPage_Unloaded;
    }

    public void OnUpdate(RepeatedTaskManager sender)
    {
        if (sender is not RRLiveRooms liveRooms) return;
        
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
        ListItemCount.Text = PlayersList.Count.ToString();
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
        if(selectedPlayer.FirstMii == null) return;
        new MiiCarouselWindow().SetMii(selectedPlayer.FirstMii).Show();
    }

    private void RoomsDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RRLiveRooms.Instance.Unsubscribe(this);
    }
    
    private void PlayerView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox listBox) return;
        listBox.ContextMenu?.Open();
    }

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
