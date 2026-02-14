using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WheelWizard.Models.RRInfo;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.Mockers;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

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

    private void ViewMiiFields_OnClick(object sender, RoutedEventArgs e)
    {
        if (PlayersListView.SelectedItem is not RrPlayer selectedPlayer)
            return;
        if (selectedPlayer.FirstMii == null)
        {
            ViewUtils.ShowSnackbar("No Mii data found for this player.", ViewUtils.SnackbarType.Warning);
            return;
        }

        var report = BuildMiiFieldsReport(selectedPlayer, selectedPlayer.FirstMii);
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText("Mii Fields")
            .SetInfoText(report)
            .SetTag("DEBUG")
            .Show();
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

    private static string BuildMiiFieldsReport(RrPlayer player, Mii mii)
    {
        var sb = new StringBuilder();

        sb.AppendLine("PLAYER");
        sb.AppendLine($"Name: {player.Name}");
        sb.AppendLine($"FriendCode: {player.FriendCode}");
        sb.AppendLine($"PID: {player.Pid}");
        sb.AppendLine($"VR: {player.VrDisplay}");
        sb.AppendLine($"BR: {player.BrDisplay}");
        sb.AppendLine($"OpenHost: {player.IsOpenHost}");
        sb.AppendLine($"Suspended: {player.IsSuspended}");
        sb.AppendLine($"Rank: {(player.LeaderboardRank?.ToString() ?? "--")}");
        sb.AppendLine();

        sb.AppendLine("MII CORE");
        sb.AppendLine($"Name: {mii.Name}");
        sb.AppendLine($"Creator: {mii.CreatorName}");
        sb.AppendLine($"IsGirl: {mii.IsGirl}");
        sb.AppendLine($"IsInvalid: {mii.IsInvalid}");
        sb.AppendLine($"Date: {mii.Date}");
        sb.AppendLine($"FavoriteColor: {mii.MiiFavoriteColor}");
        sb.AppendLine($"IsFavorite: {mii.IsFavorite}");
        sb.AppendLine($"MiiId: 0x{mii.MiiId:X8}");
        sb.AppendLine($"SystemId: 0x{mii.SystemId:X8}");
        sb.AppendLine($"IsForeign: {mii.IsForeign}");
        sb.AppendLine($"Height: {mii.Height.Value}");
        sb.AppendLine($"Weight: {mii.Weight.Value}");
        sb.AppendLine();

        sb.AppendLine("FACE");
        sb.AppendLine($"FaceShape: {mii.MiiFacialFeatures.FaceShape}");
        sb.AppendLine($"SkinColor: {mii.MiiFacialFeatures.SkinColor}");
        sb.AppendLine($"FacialFeature: {mii.MiiFacialFeatures.FacialFeature}");
        sb.AppendLine($"MingleOff: {mii.MiiFacialFeatures.MingleOff}");
        sb.AppendLine($"Downloaded: {mii.MiiFacialFeatures.Downloaded}");
        sb.AppendLine();

        sb.AppendLine("HAIR");
        sb.AppendLine($"HairType: {mii.MiiHair.HairType}");
        sb.AppendLine($"HairColor: {mii.MiiHair.MiiHairColor}");
        sb.AppendLine($"HairFlipped: {mii.MiiHair.HairFlipped}");
        sb.AppendLine();

        sb.AppendLine("EYEBROWS");
        sb.AppendLine($"Type: {mii.MiiEyebrows.Type}");
        sb.AppendLine($"Rotation: {mii.MiiEyebrows.Rotation}");
        sb.AppendLine($"Color: {mii.MiiEyebrows.Color}");
        sb.AppendLine($"Size: {mii.MiiEyebrows.Size}");
        sb.AppendLine($"Vertical: {mii.MiiEyebrows.Vertical}");
        sb.AppendLine($"Spacing: {mii.MiiEyebrows.Spacing}");
        sb.AppendLine();

        sb.AppendLine("EYES");
        sb.AppendLine($"Type: {mii.MiiEyes.Type}");
        sb.AppendLine($"Rotation: {mii.MiiEyes.Rotation}");
        sb.AppendLine($"Vertical: {mii.MiiEyes.Vertical}");
        sb.AppendLine($"Color: {mii.MiiEyes.Color}");
        sb.AppendLine($"Size: {mii.MiiEyes.Size}");
        sb.AppendLine($"Spacing: {mii.MiiEyes.Spacing}");
        sb.AppendLine();

        sb.AppendLine("NOSE/LIPS");
        sb.AppendLine($"NoseType: {mii.MiiNose.Type}");
        sb.AppendLine($"NoseSize: {mii.MiiNose.Size}");
        sb.AppendLine($"NoseVertical: {mii.MiiNose.Vertical}");
        sb.AppendLine($"LipType: {mii.MiiLips.Type}");
        sb.AppendLine($"LipColor: {mii.MiiLips.Color}");
        sb.AppendLine($"LipSize: {mii.MiiLips.Size}");
        sb.AppendLine($"LipVertical: {mii.MiiLips.Vertical}");
        sb.AppendLine();

        sb.AppendLine("GLASSES/FACIAL HAIR/MOLE");
        sb.AppendLine($"GlassesType: {mii.MiiGlasses.Type}");
        sb.AppendLine($"GlassesColor: {mii.MiiGlasses.Color}");
        sb.AppendLine($"GlassesSize: {mii.MiiGlasses.Size}");
        sb.AppendLine($"GlassesVertical: {mii.MiiGlasses.Vertical}");
        sb.AppendLine($"MustacheType: {mii.MiiFacialHair.MiiMustacheType}");
        sb.AppendLine($"BeardType: {mii.MiiFacialHair.MiiBeardType}");
        sb.AppendLine($"FacialHairColor: {mii.MiiFacialHair.Color}");
        sb.AppendLine($"MustacheSize: {mii.MiiFacialHair.Size}");
        sb.AppendLine($"MustacheVertical: {mii.MiiFacialHair.Vertical}");
        sb.AppendLine($"MoleExists: {mii.MiiMole.Exists}");
        sb.AppendLine($"MoleSize: {mii.MiiMole.Size}");
        sb.AppendLine($"MoleVertical: {mii.MiiMole.Vertical}");
        sb.AppendLine($"MoleHorizontal: {mii.MiiMole.Horizontal}");
        sb.AppendLine();

        sb.AppendLine("CUSTOM DATA");
        sb.AppendLine($"SchemaVersion: {mii.CustomDataV1.Version}");
        sb.AppendLine($"IsWheelWizardMii: {mii.CustomDataV1.IsWheelWizardMii}");
        if (mii.CustomDataV1.IsWheelWizardMii)
        {
            sb.AppendLine($"IsCopyable: {mii.CustomDataV1.IsCopyable}");
            sb.AppendLine($"AccentColor: {mii.CustomDataV1.AccentColor}");
            sb.AppendLine($"FacialExpression: {mii.CustomDataV1.FacialExpression}");
            sb.AppendLine($"CameraAngle: {mii.CustomDataV1.CameraAngle}");
            sb.AppendLine($"Tagline: {mii.CustomDataV1.Tagline}");
            sb.AppendLine($"Spare: {mii.CustomDataV1.Spare}");
        }
        else
        {
            sb.AppendLine("CustomData fields are ignored (schema/version flag not set).");
        }
        sb.AppendLine();

        var serializeResult = MiiSerializer.Serialize(mii);
        if (serializeResult.IsSuccess)
        {
            sb.AppendLine("RAW");
            sb.AppendLine($"MiiBlockHex: {Convert.ToHexString(serializeResult.Value)}");
            sb.AppendLine($"MiiBlockBase64: {Convert.ToBase64String(serializeResult.Value)}");
        }
        else
        {
            sb.AppendLine("RAW");
            sb.AppendLine($"Serialization failed: {serializeResult.Error.Message}");
        }

        return sb.ToString();
    }
}
