﻿using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using WheelWizard.Models.RRInfo;
using WheelWizard.Services.LiveData;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Pages;

public partial class RoomDetailPage : UserControl, INotifyPropertyChanged, IRepeatedTaskListener
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

    public RoomDetailPage(RrRoom room)
    {
        InitializeComponent();
        DataContext = this;
        Room = room;
        
        PlayersList = new ObservableCollection<RrPlayer>(Room.Players.Values);

        RRLiveRooms.Instance.Subscribe(this);
        PlayersListView.ItemsSource = PlayersList;
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
    }

    private void GoBackClick(object? sender, EventArgs eventArgs) => ViewUtils.NavigateToPage(new RoomsPage());

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer) return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.Fc);
    }

    private void RoomsDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        RRLiveRooms.Instance.Unsubscribe(this);
    }

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
