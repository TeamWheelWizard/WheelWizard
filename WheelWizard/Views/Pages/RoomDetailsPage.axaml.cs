using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.RRInfo;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.Mockers;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WiiManagement;

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
        PlayersList = new(Room.Players.Values);
    }

    public RoomDetailsPage(RrRoom room)
    {
        InitializeComponent();
        DataContext = this;
        Room = room;

        PlayersList = new(Room.Players.Values);

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
        foreach (var p in room.Players.Values)
        {
            PlayersList.Add(p);
        }
    }

    private void GoBackClick(object? sender, EventArgs eventArgs) => NavigationManager.NavigateTo<RoomsPage>();

    private void CopyFriendCode_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(selectedPlayer.Fc);
        ViewUtils.ShowSnackbar("Copied friend code to clipboard");
    }

    private void OpenCarousel_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        if (selectedPlayer.FirstMii == null)
            return;
        new MiiCarouselWindow().SetMii(selectedPlayer.FirstMii).Show();
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

    private void CopyMii_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!MiiDbService.Exists())
        {
            ViewUtils.ShowSnackbar("Cant save Mii", ViewUtils.SnackbarType.Warning);
            return;
        }

        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        if (selectedPlayer.FirstMii == null)
            return;

        // if (selectedPlayer.FirstMii?.CustomData.IsCopyable != true)
        // {
        //     ViewUtils.ShowSnackbar("This player doesn't want you to copy their Mii", ViewUtils.SnackbarType.Warning);
        //     return;
        // }
        var desiredMii = selectedPlayer.FirstMii;

        var macAddress = (string)SettingsManager.MACADDRESS.Get();
        //We set the miiId to 0 so it will be added as a new Mii
        desiredMii.MiiId = 0;
        var databaseResult = MiiDbService.AddToDatabase(desiredMii, macAddress);
        if (databaseResult.IsFailure)
        {
            new MessageBoxWindow()
                .SetTitleText("Failed to Copy Mii")
                .SetInfoText(databaseResult.Error!.Message)
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .Show();
            return;
        }

        ViewUtils.ShowSnackbar("Mii has been added to your Miis");
    }

    private void ContextMenu_OnOpening(object? sender, CancelEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;

        // CopyMiiButton.IsEnabled = selectedPlayer.FirstMii?.CustomData.IsCopyable == true;
    }
}
