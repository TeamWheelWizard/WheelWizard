using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using WheelWizard.Branding;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.LiveData;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Components;
using WheelWizard.Views.Pages;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Views;

public partial class Layout : BaseWindow, IRepeatedTaskListener
{
    protected override Control InteractionOverlay => DisabledDarkenEffect;
    protected override Control InteractionContent => CompleteGrid;

    public const double WindowHeight = 876;
    public const double WindowWidth = 656;
    public static Layout Instance { get; private set; } = null!;
    private const int TesterClicksRequired = 10;

    // so this is not really "Secret" its just ment to hold out people who are not meant to be testers
    // if you came here to find it, it will be useless to you, you can not actually download or play
    // testing builds since they are behind authentication walls.
    // but have fun with the beta button :)
    private const string TesterSecretPhrase = "WhenSonicInRR?";
    private int _testerClickCount;
    private bool _testerPromptOpen;
    private IDisposable? _settingsSignalSubscription;

    [Inject]
    private IBrandingSingletonService BrandingService { get; set; } = null!;

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private ISettingsSignalBus SettingsSignalBus { get; set; } = null!;

    public Layout()
    {
        Instance = this;
        InitializeComponent();
        AddLayer();

        OnSettingChanged(SettingsService.SAVED_WINDOW_SCALE);
        _settingsSignalSubscription = SettingsSignalBus.Subscribe(OnSettingSignal);
        UpdateTestingButtonVisibility();

        var completeString = Humanizer.ReplaceDynamic(Phrases.Text_MadeByString, "Patchzy", "WantToBeeMe");
        if (completeString != null && completeString.Contains("\\n"))
        {
            var split = completeString.Split("\\n");
            MadeBy_Part1.Text = split[0];
            MadeBy_Part2.Text = split[1];
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            TopBarButtons.IsVisible = false;
            TitleLabel.Margin -= new Thickness(0, 0, 0, 18);

            ExtendClientAreaTitleBarHeightHint = 0;
            SystemDecorations = SystemDecorations.Full;
            ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.PreferSystemChrome;
        }

        WhWzStatusManager.Instance.Subscribe(this);
        RRLiveRooms.Instance.Subscribe(this);
        GameLicenseService.Subscribe(this);
#if DEBUG
        KitchenSinkButton.IsVisible = true;
#endif
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        Title = BrandingService.Branding.DisplayName;
        TitleLabel.Text = BrandingService.Branding.DisplayName;

        NavigationManager.NavigateTo<HomePage>();
    }

    protected override void OnClosed(EventArgs e)
    {
        _settingsSignalSubscription?.Dispose();
        _settingsSignalSubscription = null;
        base.OnClosed(e);
    }

    private void OnSettingSignal(SettingChangedSignal signal) => OnSettingChanged(signal.Setting);

    private void OnSettingChanged(Setting setting)
    {
        // Note that this method will also be called whenever the setting changes
        if (setting == SettingsService.WINDOW_SCALE || setting == SettingsService.SAVED_WINDOW_SCALE)
        {
            var scaleFactor = (double)setting.Get();
            Height = WindowHeight * scaleFactor;
            Width = WindowWidth * scaleFactor;
            CompleteGrid.RenderTransform = new ScaleTransform(scaleFactor, scaleFactor);
            var marginXCorrection = ((scaleFactor * WindowWidth) - WindowWidth) / 2f;
            var marginYCorrection = ((scaleFactor * WindowHeight) - WindowHeight) / 2f;
            CompleteGrid.Margin = new(marginXCorrection, marginYCorrection);
            //ExtendClientAreaToDecorationsHint = scaleFactor <= 1.2f;
            return;
        }

        if (setting == SettingsService.TESTING_MODE_ENABLED)
            UpdateTestingButtonVisibility();
    }

    public void NavigateToPage(UserControl page)
    {
        ContentArea.Content = page;

        // Update the IsChecked state of the SidebarRadioButtons
        foreach (var child in SidePanelButtons.Children)
        {
            if (child is not SidebarRadioButton button)
                continue;

            var buttonPageType = button.PageType;
            button.IsChecked = buttonPageType == page.GetType();

            // TODO: make a better way to have these type of exceptions
            if (button.PageType == typeof(RoomsPage) && typeof(RoomDetailsPage) == page.GetType())
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
            case WhWzStatusManager liveAlerts:
                UpdateLiveAlert(liveAlerts);
                break;
        }
    }

    public void UpdateFriendCount()
    {
        var friends = GameLicenseService.ActiveCurrentFriends;
        FriendsButton.BoxText = $"{friends.Count(friend => friend.IsOnline)}/{friends.Count}";
        FriendsButton.BoxTip = friends.Count(friend => friend.IsOnline) switch
        {
            1 => Phrases.Hover_FriendsOnline_1,
            0 => Phrases.Hover_FriendsOnline_0,
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_FriendsOnline_x, friends.Count(friend => friend.IsOnline))
                ?? $"There are currently {friends.Count(friend => friend.IsOnline)} friends online",
        };
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
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_PlayersOnline_x, playerCount)
                ?? $"There are currently {playerCount} players online",
        };
        RoomCountBox.Text = roomCount.ToString();
        RoomCountBox.TipText = roomCount switch
        {
            1 => Phrases.Hover_RoomsOnline_1,
            0 => Phrases.Hover_RoomsOnline_0,
            _ => Humanizer.ReplaceDynamic(Phrases.Hover_RoomsOnline_x, roomCount) ?? $"There are currently {roomCount} rooms active",
        };
        UpdateFriendCount();
    }

    private void UpdateLiveAlert(WhWzStatusManager sender)
    {
        var hasVariant = sender.Status?.Variant != null && sender.Status.Variant != WhWzStatusVariant.None;
        var hasCustomIcon = !string.IsNullOrEmpty(sender.Status?.Icon);
        var visible = hasVariant || hasCustomIcon;

        LiveStatusBorder.IsVisible = visible;
        if (!visible)
            return;

        ToolTip.SetTip(LiveStatusBorder, sender.Status!.Message);
        LiveStatusBorder.Classes.Clear();

        // If custom icon is provided, use it instead of variant
        if (hasCustomIcon)
        {
            // Clear any variant-based classes
            LiveStatusBorder.Classes.Add("Custom");

            // Find the PathIcon in the LiveStatusBorder and update it dynamically
            if (LiveStatusBorder.Child is PathIcon pathIcon)
            {
                // Parse the SVG path data
                var geometry = Geometry.Parse(sender.Status.Icon!);
                pathIcon.Data = geometry;

                // Apply custom color if provided, otherwise use a default
                if (!string.IsNullOrEmpty(sender.Status.Color))
                {
                    pathIcon.Foreground = new SolidColorBrush(Color.Parse(sender.Status.Color));
                }
                else
                {
                    pathIcon.Foreground = new SolidColorBrush(Colors.White);
                }
            }
        }
        else
        {
            // Use variant-based styling
            LiveStatusBorder.Classes.Add(sender.Status.Variant.ToString()!);
        }
    }

    private void TopBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private async void TitleLabel_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        e.Handled = true;

        if (SettingsService.Get<bool>(SettingsService.TESTING_MODE_ENABLED))
            return;

        if (_testerPromptOpen)
            return;

        _testerClickCount++;
        if (_testerClickCount < TesterClicksRequired)
            return;

        _testerClickCount = 0;
        _testerPromptOpen = true;

        try
        {
            var result = await new TextInputWindow()
                .SetMainText("Welcome tester, write your secret phrase")
                .SetPlaceholderText("Secret phrase")
                .SetButtonText("Cancel", "Submit")
                .ShowDialog();

            if (string.IsNullOrWhiteSpace(result))
                return;

            if (result == TesterSecretPhrase)
            {
                SettingsService.Set(SettingsService.TESTING_MODE_ENABLED, true);
                ShowSnackbar("Testing mode enabled", ViewUtils.SnackbarType.Success);
            }
            else
            {
                ShowSnackbar("Incorrect secret phrase", ViewUtils.SnackbarType.Danger);
            }
        }
        finally
        {
            _testerPromptOpen = false;
        }
    }

    private void UpdateTestingButtonVisibility()
    {
        TestingButton.IsVisible = SettingsService.Get<bool>(SettingsService.TESTING_MODE_ENABLED);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Discord_Click(object sender, EventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.DiscordUrl.ToString());

    private void Github_Click(object sender, EventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.RepositoryUrl.ToString());

    private void Support_Click(object sender, EventArgs e) => ViewUtils.OpenLink(BrandingService.Branding.SupportUrl.ToString());

    private void CloseSnackbar_OnClick(object? sender, EventArgs e)
    {
        Snackbar.Classes.Remove("show");
        Snackbar.IsVisible = false;
    }

    public void ShowSnackbar(string message, ViewUtils.SnackbarType type)
    {
        Snackbar.Classes.Clear();

        SnackbarText.Text = message;
        Snackbar.Classes.Add("show");
        Snackbar.Classes.Add(type.ToString().ToLower());
    }
}
