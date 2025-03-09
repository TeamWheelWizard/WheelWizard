using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using WheelWizard.Helpers;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement.SaveData;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Components.StandardLibrary;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views;

public partial class Layout : BaseWindow, IRepeatedTaskListener, ISettingListener
{
    protected override Control InteractionOverlay => DisabledDarkenEffect;
    protected override Control InteractionContent => CompleteGrid;
    
    public const double WindowHeight = 876;
    public const double WindowWidth = 656;
    public static Layout Instance { get; private set; }
    
    private UserControl _currentPage;
    
    public Layout()
    {
        Instance = this;
        Initialized += ViewUtils.OnInitialized;
        InitializeComponent();
        AddLayer();
        
        OnSettingChanged(SettingsManager.SAVED_WINDOW_SCALE);
        SettingsManager.WINDOW_SCALE.Subscribe(this);
        
        var completeString = Humanizer.ReplaceDynamic(Phrases.Text_MadeByString, "Patchzy", "WantToBeeMe" );
        if (completeString != null && completeString.Contains("\\n"))
        {
            var split = completeString.Split("\\n");
            MadeBy_Part1.Text = split[0];
            MadeBy_Part2.Text = split[1];
        }
        
        NavigateToPage(new HomePage());
        InitializeManagers();
        
    #if DEBUG
        KitchenSinkButton.IsVisible = true;
    #endif
    }

    public void OnSettingChanged(Setting setting)
    {
        // Note that this method will also be called whenever the setting changes
        var scaleFactor = (double)setting.Get();
        Height = WindowHeight * scaleFactor;
        Width = WindowWidth * scaleFactor;
        CompleteGrid.RenderTransform = new ScaleTransform(scaleFactor, scaleFactor);
        var marginXCorrection = ((scaleFactor * WindowWidth) - WindowWidth)/2f;
        var marginYCorrection = ((scaleFactor * WindowHeight) - WindowHeight)/2f;
        CompleteGrid.Margin = new Thickness(marginXCorrection, marginYCorrection);
        //ExtendClientAreaToDecorationsHint = scaleFactor <= 1.2f;
    }

    private void InitializeManagers()
    {
        LiveAlertsManager.Instance.Subscribe(this);
        LiveAlertsManager.Instance.Start(); // Temporary code, should be moved to a more appropriate location
        RRLiveRooms.Instance.Subscribe(this);
        RRLiveRooms.Instance.Start(); // Temporary code, should be moved to a more appropriate location
        GameDataLoader.Instance.Subscribe(this);
        GameDataLoader.Instance.Start(); // Temporary code, should be moved to a more appropriate location
    }
    
    public void NavigateToPage(UserControl page)
    {
        ContentArea.Content = page;

        // Update the IsChecked state of the SidebarRadioButtons
        foreach (var child in SidePanelButtons.Children)
        {
            if (child is not SidebarRadioButton button) continue;

            var buttonPageType = button.PageType; 
            button.IsChecked = buttonPageType == page.GetType();
            
            // TODO: make a better way to have these type of exceptions
            if(button.PageType == typeof(RoomsPage) && typeof(RoomDetailsPage) == page.GetType())
                button.IsChecked = true;
        }
    }
    public void OnUpdate(RepeatedTaskManager sender)
    {
        switch (sender)
        {
            case RRLiveRooms liveRooms:
                UpdatePlayerAndRoomCount(liveRooms);
                break;
            case LiveAlertsManager liveAlerts:
                UpdateLiveAlert(liveAlerts);
                break;
            
        }
    }

    public void UpdatePlayerAndRoomCount(RRLiveRooms sender)
    {
        var playerCount = sender.PlayerCount;
        var roomCount = sender.RoomCount;
        PlayerCountBox.Text = playerCount.ToString();
        PlayerCountBox.TipText = playerCount switch
        {
            1 => Phrases.Hover_PlayersOnline_1,
            0 => Phrases.Hover_PlayersOnline_0,
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_PlayersOnline_x, playerCount) ?? 
                 $"There are currently {playerCount} players online"
        };
        RoomCountBox.Text = roomCount.ToString();
        RoomCountBox.TipText = roomCount switch
        {
            1 => Phrases.Hover_RoomsOnline_1,
            0 => Phrases.Hover_RoomsOnline_0,
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_RoomsOnline_x, roomCount) ?? 
                 $"There are currently {roomCount} rooms active"
        };
        var friends = GameDataLoader.Instance.GetCurrentFriends;
        FriendsButton.BoxText =$"{friends.Count(friend => friend.IsOnline)}/{friends.Count}";
        FriendsButton.BoxTip = friends.Count(friend => friend.IsOnline) switch
        {
            1 => Phrases.Hover_FriendsOnline_1,
            0 => Phrases.Hover_FriendsOnline_0,
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_FriendsOnline_x, friends.Count(friend => friend.IsOnline)) ?? 
                 $"There are currently {friends.Count(friend => friend.IsOnline)} friends online"
        };
    }
     
    private void UpdateLiveAlert(LiveAlertsManager sender)
    {
        var visible = sender.Status != null && sender.Status.StatusVariant != LiveAlertsManager.LiveStatusVariant.None;
        LiveStatusBorder.IsVisible = visible;
        if (!visible) return;
        
        ToolTip.SetTip(LiveStatusBorder, sender.Status!.Message);
        LiveStatusBorder.Classes.Clear();
        LiveStatusBorder.Classes.Add(sender.Status!.StatusVariant.ToString());
    }
    
    private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();
    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Discord_Click(object sender, EventArgs e) => ViewUtils.OpenLink(Endpoints.WhWzDiscordUrl);
    private void Github_Click(object sender, EventArgs e) =>  ViewUtils.OpenLink(Endpoints.WhWzGithubUrl);
    private void Support_Click(object sender, EventArgs e) => ViewUtils.OpenLink(Endpoints.SupportLink);
}
