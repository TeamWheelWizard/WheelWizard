using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.ObjectModel;
using System.ComponentModel;
using WheelWizard.Models.RRInfo;
using WheelWizard.Services.LiveData;
using WheelWizard.Utilities.RepeatedTasks;

namespace WheelWizard.Views.Pages;

public partial class RoomsPage : UserControl, INotifyPropertyChanged, IRepeatedTaskListener
{
    private string? _searchQuery;
    
    private readonly ObservableCollection<RrRoom> _rooms = new();
    public ObservableCollection<RrRoom> Rooms
    {
        get => _rooms;
        init
        {
            _rooms = value;
            OnPropertyChanged(nameof(Rooms));
        }
    }

    private readonly ObservableCollection<RrPlayer> _players = new();
    public ObservableCollection<RrPlayer> Players
    {
        get => _players;
        init
        {
            _players = value;
            OnPropertyChanged(nameof(Players));
        }
    }

    public RoomsPage()
    {
        InitializeComponent();
        DataContext = this;
        RRLiveRooms.Instance.Subscribe(this);
        
        OnUpdate(RRLiveRooms.Instance);
        Unloaded += RoomsPage_Unloaded;
    }
    
    public void OnUpdate(RepeatedTaskManager sender)
    {
        if (sender is not RRLiveRooms liveRooms) return;

        Rooms.Clear();
        var count = liveRooms.RoomCount;
        EmptyRoomsView.IsVisible = count == 0;
        RoomsListViewContainer.IsVisible = count != 0;
        if (count == 0) return;

        foreach (var room in liveRooms.CurrentRooms)
            Rooms.Add(room);
        
        RoomsNiceLabel.IsVisible = liveRooms.CurrentRooms.Count == 69;
        RoomsListItemCount.Text = liveRooms.CurrentRooms.Count.ToString();
        PerformSearch(_searchQuery);
    }
    
    private void PerformSearch(string? query)
    {
        var isStringEmpty = string.IsNullOrWhiteSpace(query);
        RoomsListViewContainer.IsVisible = isStringEmpty;
        PlayerListViewContainer.IsVisible = !isStringEmpty;
        
        if (isStringEmpty) return;

        Players.Clear();
        var matchingPlayers = Rooms
            .SelectMany(r => r.Players.Values)
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        p.Fc.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        foreach (var player in matchingPlayers)
            Players.Add(player);
        
        PlayerNiceLabel.IsVisible = matchingPlayers.Count == 69;
        PlayerListItemCount.Text = matchingPlayers.Count.ToString();
    }
    
    private void RoomsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RRLiveRooms.Instance.Unsubscribe(this);
    }
    
    private void PlayerSearchField_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (e.Source is not TextBox textBox) return;
        _searchQuery = textBox.Text;
        PerformSearch(textBox.Text);
    }
    
    private void RoomsView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox listBox) return;
        if (listBox.SelectedItem is not RrRoom selectedRoom) return;
        
        ViewUtils.NavigateToPage(new RoomDetailsPage(selectedRoom));
        listBox.SelectedItem = null;
        // Deselect the item immediately after navigating. This is important
        // for a good user experience. Otherwise, the item stays selected,
        // and if you navigate back, you can't re-select the same item.
    }

    private void PlayerView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not ListBox listBox) return;
        if (listBox.SelectedItem is not RrPlayer selectedRoom) return;

        var room = Rooms.FirstOrDefault(r => r.Players.ContainsValue(selectedRoom));
        ViewUtils.NavigateToPage(new RoomDetailsPage(room));
        listBox.SelectedItem = null;
        // Deselect the item immediately after navigating. This is important
        // for a good user experience. Otherwise, the item stays selected,
        // and if you navigate back, you can't re-select the same item.
    }

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
