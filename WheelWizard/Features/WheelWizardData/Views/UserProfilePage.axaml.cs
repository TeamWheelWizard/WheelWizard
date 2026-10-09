using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using WheelWizard.CustomCharacters;
using WheelWizard.CustomDistributions;
using WheelWizard.Models.Enums;
using WheelWizard.RrRooms;
using WheelWizard.RrRooms.Views;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Components;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Views;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;
using WheelWizard.WiiManagement.MiiManagement.Views.Dialogs;

namespace WheelWizard.WheelWizardData.Views;

public partial class UserProfilePage : UserControl, INotifyPropertyChanged
{
    private ICustomCharactersService CustomCharacters { get; }

    private INavigationService Navigation { get; }

    private LicenseProfile? currentPlayer;
    private Mii? _currentMii;
    private bool _isOnline;
    private bool _hasCurrentUserRoom;
    private bool _isPrimary;
    private string _currentFriendCode = string.Empty;

    private LiveRoomsService LiveRooms { get; }

    private VrHistoryGraph HistoryGraph { get; }

    /// <summary>The licenses with a Mii, in slot order, peeking over the profile card.</summary>
    public ObservableCollection<LicenseTab> LicenseTabs { get; } = [];

    private IGameLicenseSingletonService GameLicenseService { get; }

    private IWhWzDataSingletonService BadgeService { get; }

    private IMiiDbService MiiDbService { get; }

    private ISettingsManager SettingsService { get; }

    private ISaveRegionService SaveRegions { get; }

    private ICustomDistributionPaths DistributionPaths { get; }

    public Mii? CurrentMii
    {
        get => _currentMii;
        set
        {
            _currentMii = value;
            OnPropertyChanged(nameof(CurrentMii));
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            _isOnline = value;
            OnPropertyChanged(nameof(IsOnline));
        }
    }

    public bool HasCurrentUserRoom
    {
        get => _hasCurrentUserRoom;
        set
        {
            _hasCurrentUserRoom = value;
            OnPropertyChanged(nameof(HasCurrentUserRoom));
        }
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set
        {
            _isPrimary = value;
            OnPropertyChanged(nameof(IsPrimary));
        }
    }

    public string CurrentFriendCode
    {
        get => _currentFriendCode;
        set
        {
            _currentFriendCode = value;
            OnPropertyChanged(nameof(CurrentFriendCode));
        }
    }

    private int _currentUserIndex;
    private int FocusedUser => SettingsService.Get<int>(SettingsService.FOCUSED_USER);

    #region Mii animations

    /// <summary>The first time the page opens the Mii takes its time to come to the window; after that it's quick.</summary>
    private static bool _hasGreeted;

    private static readonly string[] Greetings = ["profile/window/hello", "profile/window/funny"];
    private static readonly string[] PopUp = ["profile/quick_appear/from_below"];
    private static readonly string[] DropDown = ["profile/quick_exit/to_below"];

    /// <summary>How often switching licenses pops the Mii up from below instead of sliding it in from the side.</summary>
    private const double PopUpChance = 0.3;

    private void GreetWithMii()
    {
        ProfileMii.ArriveWith(_hasGreeted ? PopUp : Greetings);
        _hasGreeted = true;
    }

    /// <summary>
    /// The current Mii leaves and the next one comes in, like a carousel: going to a license on the right, the Mii
    /// slides out to the left and the next one comes in from the right (and the other way round).
    /// </summary>
    private void SwapMii(int direction)
    {
        if (direction == 0 || Random.Shared.NextDouble() < PopUpChance)
            ProfileMii.ArriveWith(PopUp, DropDown);
        else if (direction > 0)
            ProfileMii.ArriveWith(["profile/quick_appear/from_right"], ["profile/quick_exit/to_left"]);
        else
            ProfileMii.ArriveWith(["profile/quick_appear/from_left"], ["profile/quick_exit/to_right"]);
    }

    #endregion

    public UserProfilePage(
        VrHistoryGraph historyGraph,
        ICustomCharactersService customCharacters,
        INavigationService navigation,
        LiveRoomsService liveRooms,
        IGameLicenseSingletonService gameLicenseService,
        IWhWzDataSingletonService badgeService,
        IMiiDbService miiDbService,
        ISettingsManager settingsService,
        ISaveRegionService saveRegions,
        ICustomDistributionPaths distributionPaths
    )
    {
        CustomCharacters = customCharacters;
        Navigation = navigation;
        LiveRooms = liveRooms;
        GameLicenseService = gameLicenseService;
        BadgeService = badgeService;
        MiiDbService = miiDbService;
        SettingsService = settingsService;
        SaveRegions = saveRegions;
        DistributionPaths = distributionPaths;
        HistoryGraph = historyGraph;
        InitializeComponent();
        HistoryHost.Content = historyGraph;
        historyGraph.Bind(VrHistoryGraph.FriendCodeProperty, new Binding("CurrentFriendCode") { Source = this });
        _currentUserIndex = FocusedUser;
        PopulateRegions();
        GreetWithMii();
        UpdatePage();
        DataContext = this;
        // Make sure this action gets subscribed AFTER the PopulateRegions method
        RegionDropdown.SelectionChanged += RegionDropdown_SelectionChanged;
    }

    internal void RefreshSelectedProfile()
    {
        RegionDropdown.SelectionChanged -= RegionDropdown_SelectionChanged;
        try
        {
            RegionDropdown.Items.Clear();
            PopulateRegions();
            _currentUserIndex = FocusedUser;
            UpdatePage();
        }
        finally
        {
            RegionDropdown.SelectionChanged += RegionDropdown_SelectionChanged;
        }
    }

    private void PopulateRegions()
    {
        var validRegions = SaveRegions.GetAvailableRegions(DistributionPaths.SaveFolderPath);
        var currentRegion = SettingsService.Get<MarioKartWiiEnums.Regions>(SettingsService.RR_REGION);
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>())
        {
            if (region == MarioKartWiiEnums.Regions.None)
                continue;

            var name = region switch
            {
                MarioKartWiiEnums.Regions.Europe => t("region.europe"),
                MarioKartWiiEnums.Regions.America => t("region.america"),
                MarioKartWiiEnums.Regions.Korea => t("region.south_korea"),
                MarioKartWiiEnums.Regions.Japan => t("region.japan"),
                _ => t("state.unknown"),
            };
            var itemForRegionDropdown = new ComboBoxItem
            {
                Content = name,
                Tag = region,
                IsEnabled = validRegions.Contains(region),
            };
            RegionDropdown.Items.Add(itemForRegionDropdown);

            if (currentRegion == region)
                RegionDropdown.SelectedItem = itemForRegionDropdown;
        }
    }

    #region Update page

    private void RefreshLicenseTabs()
    {
        var validUsers = GameLicenseService.HasAnyValidUsers;
        ProfileContent.IsVisible = validUsers;
        NoProfilesInfo.IsVisible = !validUsers;
        if (!validUsers)
        {
            CurrentFriendCode = string.Empty;
            HasCurrentUserRoom = false;
            IsOnline = false;
        }

        var licenses = GameLicenseService
            .LicenseCollection.Users.Select((user, index) => (User: user, Index: index))
            .Where(license => (license.User.Mii?.Name.ToString() ?? SettingValues.NoName) != SettingValues.NoLicense)
            .ToList();

        // Update the peekers in place, so their Miis keep going; only a different set of licenses starts over.
        if (!licenses.Select(license => license.Index).SequenceEqual(LicenseTabs.Select(tab => tab.Index)))
        {
            LicenseTabs.Clear();
            foreach (var license in licenses)
                LicenseTabs.Add(new LicenseTab(license.Index));
        }

        foreach (var (tab, (user, index)) in LicenseTabs.Zip(licenses))
        {
            var miiName = user.Mii?.Name.ToString() ?? SettingValues.NoName;
            tab.DisplayName = miiName == SettingValues.NoName ? t("state.no_name") : miiName;
            tab.Mii = user.Mii;
            tab.IsPrimary = index == FocusedUser;
            tab.IsSelected = index == _currentUserIndex;
        }
    }

    private void UpdatePage()
    {
        currentPlayer = GameLicenseService.GetUserData(_currentUserIndex);
        IsPrimary = FocusedUser == _currentUserIndex;
        CurrentFriendCode = currentPlayer.FriendCode;
        ProfileAttribFriendCode.Text = currentPlayer.FriendCode;
        FriendCodeRow.IsVisible = !string.IsNullOrEmpty(currentPlayer.FriendCode);
        ProfileAttribUserName.Text = currentPlayer.NameOfMii;
        HistoryGraph.CurrentVr = currentPlayer.Vr.ToString("N0");
        HistoryGraph.Wins = currentPlayer.Statistics.Performance.FirstPlaces.ToString("N0");
        HistoryGraph.RacesPlayed = currentPlayer.Statistics.RaceTotals.AllRacesCount.ToString("N0");
        CurrentMii = currentPlayer.Mii;
        IsOnline = currentPlayer.IsOnline;
        HasCurrentUserRoom = IsUserInLiveRoom(currentPlayer.FriendCode);

        BadgeContainer.Children.Clear();
        var badges = BadgeService.GetBadges(currentPlayer.FriendCode).Select(variant => new CommunityBadge { Variant = variant });
        foreach (var badge in badges)
        {
            badge.Height = 26;
            badge.Width = 26;
            BadgeContainer.Children.Add(badge);
        }

        RefreshLicenseTabs();
    }

    #endregion

    private void SetUserAsPrimary()
    {
        if (FocusedUser == _currentUserIndex)
            return;

        SettingsService.Set(SettingsService.FOCUSED_USER, _currentUserIndex);
        IsPrimary = true;
        RefreshLicenseTabs();

        //now we refresh the sidebar friend amount
        var layout = ViewUtils.GetLayout();
        layout.UpdateFriendCount();
        layout.UpdateSidebarProfile();
        ViewUtils.ShowSnackbar(t("snackbar_success.profile_set_primary"));
    }

    private void RegionDropdown_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (RegionDropdown.SelectedItem is not ComboBoxItem { Tag: MarioKartWiiEnums.Regions region })
            return;

        SettingsService.Set(SettingsService.RR_REGION, region);
        var loadResult = GameLicenseService.LoadLicense();
        if (loadResult.IsFailure)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to load game data")
                .SetInfoText(loadResult.Error.Message)
                .Show();
            return;
        }

        _currentUserIndex = 0; // Just in case you have current user set as 4. and you change to a region where there are only 3 users.
        SetUserAsPrimary();
        SwapMii(0);
        UpdatePage();
        var layout = ViewUtils.GetLayout();
        layout.UpdateFriendCount();
        layout.UpdateSidebarProfile();
    }

    private void LicensePeeker_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LicenseTab tab } || tab.Index == _currentUserIndex)
            return;

        var oldIndex = _currentUserIndex;
        _currentUserIndex = tab.Index;
        SwapMii(tab.Index - oldIndex);
        UpdatePage();
    }

    private void MakePrimary_OnClick(object? sender, RoutedEventArgs e)
    {
        if (FocusedUser == _currentUserIndex)
            return;

        SetUserAsPrimary();
        ProfileMii.Play("profile/make_primary");
    }

    private async void OpenMiiSelector_Click(object? sender, RoutedEventArgs e)
    {
        var availableMiis = MiiDbService.GetAllMiis();
        if (!availableMiis.Any())
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_NoMiisFound);
            return;
        }

        var selectedMii = await new MiiSelectorWindow()
            .SetMiiOptions(availableMiis, CurrentMii, SettingsService.Get<string>(SettingsService.MACADDRESS))
            .AwaitAnswer();

        if (selectedMii == null)
            return;

        var result = GameLicenseService.ChangeMii(_currentUserIndex, selectedMii);

        if (result.IsFailure)
        {
            new MessageBoxWindow()
                .SetTitleText(t("message_error.failed_change_mii.title"))
                .SetInfoText(result.Error!.Message)
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .Show();
            return;
        }

        SwapMii(0);
        CurrentMii = selectedMii;
        GameLicenseService.LoadLicense();
        UpdatePage();
        UpdateSidebarProfileIfCurrentUser();
        ViewUtils.ShowSnackbar(t("message_success.mii_changed"));
    }

    private void ViewRoom_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var room in LiveRooms.CurrentRooms)
        {
            if (room.Players.All(player => player.FriendCode != currentPlayer?.FriendCode))
                continue;

            Navigation.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CouldNotFindRoom);
    }

    private void CopyFriendCode_OnClick(object? sender, RoutedEventArgs e)
    {
        if (currentPlayer?.FriendCode == null)
            return;

        TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(currentPlayer.FriendCode);
        ViewUtils.ShowSnackbar(t("snackbar_success.copied_fc"));
    }

    // This is intentionally a separate validation method besides the true name validation. That name validation allows less than 3.
    // But we as team wheel wizard don't think it makes sense to have a mii name shorter than 3, and so from the UI we don't allow it
    private OperationResult ValidateMiiName(string? oldName, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (newName.Length is > 10 or < 3)
            return Fail(t("helper_note.name_must_between"));

        return Ok();
    }

    private async void RenameMii_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldName = CurrentMii?.Name.ToString();
        var extraText = t("question.enter_new_name.extra", new { name = oldName ?? string.Empty }) ?? string.Empty;
        var renamePopup = new TextInputWindow()
            .SetMainText(t("question.enter_new_name.title"))
            .SetExtraText(extraText)
            .SetCustomCharacters(CustomCharacters.GetCustomCharacters())
            .SetValidation(ValidateMiiName)
            .SetInitialText(oldName ?? "")
            .SetPlaceholderText(oldName ?? "");

        var newName = await renamePopup.ShowDialog();
        if (oldName == newName || newName == null)
            return;
        var changeNameResult = GameLicenseService.ChangeMiiName(_currentUserIndex, newName);
        if (changeNameResult.IsFailure)
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("message_error.failed_change_name.title"))
                .SetInfoText(changeNameResult.Error.Message)
                .Show();
        else
            ViewUtils.ShowSnackbar(t("snackbar_success.name_change", new { name = newName }) ?? "Name changed successfully");

        //reload game data, since multiple licenses can use the same mii
        GameLicenseService.LoadLicense();
        UpdatePage();
        UpdateSidebarProfileIfCurrentUser();
        if (changeNameResult.IsSuccess)
            ProfileMii.Play("profile/rename");
    }

    private void UpdateSidebarProfileIfCurrentUser()
    {
        if (FocusedUser != _currentUserIndex)
            return;

        ViewUtils.GetLayout().UpdateSidebarProfile();
    }

    private bool IsUserInLiveRoom(string? friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return false;

        return LiveRooms.CurrentRooms.Any(room => room.Players.Any(player => player.FriendCode == friendCode));
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}

/// <summary>A license with a Mii, peeking over the profile card.</summary>
public sealed class LicenseTab(int index) : INotifyPropertyChanged
{
    private Mii? _mii;
    private string _displayName = string.Empty;
    private bool _isPrimary;
    private bool _isSelected;

    /// <summary>The license slot (0-3).</summary>
    public int Index { get; } = index;

    public Mii? Mii
    {
        get => _mii;
        set => Set(ref _mii, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => Set(ref _displayName, value);
    }

    public bool IsPrimary
    {
        get => _isPrimary;
        set => Set(ref _isPrimary, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new(propertyName));
    }
}
