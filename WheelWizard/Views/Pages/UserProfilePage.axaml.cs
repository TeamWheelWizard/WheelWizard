using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.Models.Enums;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Components;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.MiiManagement;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public partial class UserProfilePage : UserControlBase, INotifyPropertyChanged
{
    private const int ProfileCarouselPageCount = 2;
    private const int ProfileSelectorMaxCharacters = 12;

    private LicenseProfile? currentPlayer;
    private Mii? _currentMii;
    private bool _isOnline;
    private bool _hasCurrentUserRoom;
    private bool _hasProfileInfo;
    private string _currentFriendCode = string.Empty;
    private int _activeInfoSlideIndex;
    private List<ProfileLibraryEntry> _allLibraryProfiles = [];
    private List<ProfileLibraryEntry> _visibleLibraryProfiles = [];
    private ProfileLibraryEntry? _cloudProfileBeingViewed;

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    [Inject]
    private IWhWzDataSingletonService BadgeService { get; set; } = null!;

    [Inject]
    private IMiiDbService MiiDbService { get; set; } = null!;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private ICloudProfileLibraryService ProfileLibraryService { get; set; } = null!;

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

    public bool HasProfileInfo
    {
        get => _hasProfileInfo;
        set
        {
            _hasProfileInfo = value;
            OnPropertyChanged(nameof(HasProfileInfo));
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

    public int ActiveInfoSlideIndex
    {
        get => _activeInfoSlideIndex;
        set
        {
            var normalizedIndex = NormalizeCarouselIndex(value);
            if (_activeInfoSlideIndex == normalizedIndex)
                return;

            _activeInfoSlideIndex = normalizedIndex;
            OnPropertyChanged(nameof(ActiveInfoSlideIndex));
            UpdateCarouselIndicators();
        }
    }

    private int _currentUserIndex;
    private int FocusedUser => SettingsService.Get<int>(SettingsService.FOCUSED_USER);

    public UserProfilePage()
    {
        InitializeComponent();
        ResetMiiTopBar();
        ViewMii(FocusedUser);
        PopulateRegions();
        UpdatePage();
        DataContext = this;
        UpdateCarouselIndicators();
        _ = RefreshVisibleProfileLibraryAsync();
        ProfileLibraryChangeNotifier.Changed += ProfileLibraryChanged_OnChanged;
        DetachedFromVisualTree += (_, _) => DeactivateProfileLibrarySubscription();
        // Make sure this action gets subscribed AFTER the PopulateRegions method
        RegionDropdown.SelectionChanged += RegionDropdown_SelectionChanged;
    }

    internal void DeactivateProfileLibrarySubscription() => ProfileLibraryChangeNotifier.Changed -= ProfileLibraryChanged_OnChanged;

    private void ProfileLibraryChanged_OnChanged(object? sender, EventArgs e)
    {
        // The game process has exited at this point. Reload the same singleton used by the
        // sidebar and redraw this existing page, rather than requiring a complete app restart.
        Dispatcher.UIThread.Post(async () =>
        {
            if (GameLicenseService.LoadLicense().IsFailure)
                return;
            _currentUserIndex = Math.Clamp(_currentUserIndex, 0, 3);
            UpdatePage();
            await RefreshVisibleProfileLibraryAsync();
            var layout = ViewUtils.GetLayout();
            layout.UpdateFriendCount();
            layout.UpdateSidebarProfile();
        });
    }

    private void PopulateRegions()
    {
        var validRegions = RRRegionManager.GetValidRegions();
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

    private void ResetMiiTopBar()
    {
        var validUsers = GameLicenseService.HasAnyValidUsers;
        HasProfileInfo = validUsers;
        CurrentUserProfile.IsVisible = validUsers;
        ProfileCarouselContainer.IsVisible = validUsers;
        NoProfilesInfo.IsVisible = !validUsers;
        if (!validUsers)
        {
            CurrentFriendCode = string.Empty;
            HasCurrentUserRoom = false;
            IsOnline = false;
            UpdateOnlineBorders();
            ActiveInfoSlideIndex = 0;
        }

        var data = GameLicenseService.LicenseCollection;
        var userAmount = data.Users.Count;
        for (var i = 0; i < userAmount; i++)
        {
            var radioButton = RadioButtons.Children[i] as RadioButton;
            if (radioButton == null!)
                continue;

            var miiName = data.Users[i].Mii?.Name.ToString() ?? SettingValues.NoName;
            var noLicense = miiName == SettingValues.NoLicense;

            radioButton.IsEnabled = !noLicense;
            var displayName = miiName switch
            {
                SettingValues.NoName => t("state.no_name"),
                SettingValues.NoLicense => t("state.no_license"),
                _ => miiName,
            };
            radioButton.Content = TrimProfileSelectorText(displayName);
        }

        if (SettingsService.Get<bool>(SettingsService.CLOUD_SYNC_ENABLED) && _visibleLibraryProfiles.Count > 0)
            ApplyVisibleProfileSelectors();

        UpdateCarouselIndicators();
    }

    private static string TrimProfileSelectorText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < ProfileSelectorMaxCharacters)
            return text ?? string.Empty;

        return $"{text[..(ProfileSelectorMaxCharacters - 1)]}...";
    }

    private void UpdatePage()
    {
        _cloudProfileBeingViewed = null;
        PrimaryCheckBox.IsChecked = FocusedUser == _currentUserIndex;

        currentPlayer = GameLicenseService.GetUserData(_currentUserIndex);
        CurrentFriendCode = currentPlayer.FriendCode;
        ProfileAttribFriendCode.Text = currentPlayer.FriendCode;
        ProfileAttribFriendCode.IsVisible = !string.IsNullOrEmpty(currentPlayer.FriendCode);
        ProfileAttribUserName.Text = currentPlayer.NameOfMii;
        ProfileAttribVr.Text = currentPlayer.Vr.ToString();
        ProfileAttribBr.Text = currentPlayer.Br.ToString();
        CurrentMii = currentPlayer.Mii;
        IsOnline = currentPlayer.IsOnline;
        HasCurrentUserRoom = IsUserInLiveRoom(currentPlayer.FriendCode);
        SetLocalProfileControlsVisible(true);
        UpdateOnlineBorders();

        ProfileAttribTotalRaces.Text = currentPlayer.Statistics.RaceTotals.AllRacesCount.ToString();
        ProfileAttribTotalWins.Text = currentPlayer.Statistics.Performance.FirstPlaces.ToString();

        BadgeContainer.Children.Clear();
        var badges = BadgeService.GetBadges(currentPlayer.FriendCode).Select(variant => new Badge { Variant = variant });
        foreach (var badge in badges)
        {
            badge.Height = 30;
            badge.Width = 30;
            BadgeContainer.Children.Add(badge);
        }

        ResetMiiTopBar();
    }

    #endregion

    private void ViewMii(int? mii = null)
    {
        _currentUserIndex = mii ?? _currentUserIndex;
        if (RadioButtons.Children[_currentUserIndex] is RadioButton radioButton)
            radioButton.IsChecked = true;
    }

    private void SetUserAsPrimary()
    {
        if (FocusedUser == _currentUserIndex)
            return;

        SettingsService.Set(SettingsService.FOCUSED_USER, _currentUserIndex);

        PrimaryCheckBox.IsChecked = true;
        // Even though it's true when this method is called, we still set it to true,
        // since Avalonia has some weird ass cashing, It might just be that that is because this method is actually deprecated

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
        ResetMiiTopBar();
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

        ViewMii(0); // Just in case you have current user set as 4. and you change to a region where there are only 3 users.
        SetUserAsPrimary();
        UpdatePage();
        var layout = ViewUtils.GetLayout();
        layout.UpdateFriendCount();
        layout.UpdateSidebarProfile();
    }

    private void TopBarRadio_OnClick(object? sender, RoutedEventArgs e)
    {
        var oldIndex = _currentUserIndex;

        if (sender is RadioButton { Tag: ProfileLibraryEntry profile })
        {
            if (profile.Source is ProfileLibrarySource.Cloud or ProfileLibrarySource.Vault)
            {
                ShowCloudProfile(profile);
                return;
            }

            if (profile.LocalSlot is not int localSlot)
                return;
            _currentUserIndex = localSlot;
            if (oldIndex != _currentUserIndex || _cloudProfileBeingViewed is not null)
                UpdatePage();
            return;
        }

        if (sender is not RadioButton button || !int.TryParse((string?)button.Tag, out _currentUserIndex))
            return;
        if (oldIndex == _currentUserIndex)
            return;

        UpdatePage();
    }

    private void CheckBox_SetPrimaryUser(object sender, RoutedEventArgs e) => ViewUtils.IfChecked(sender, () => SetUserAsPrimary());

    private void PrevCarouselPage_OnClick(object? sender, RoutedEventArgs e) => MoveCarouselPage(-1);

    private void NextCarouselPage_OnClick(object? sender, RoutedEventArgs e) => MoveCarouselPage(1);

    private async void OpenMiiSelector_Click(object? sender, RoutedEventArgs e)
    {
        var availableMiis = MiiDbService.GetAllMiis();
        if (!availableMiis.Any())
        {
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_NoMiisFound);
            return;
        }

        var selectedMii = await new MiiSelectorWindow().SetMiiOptions(availableMiis, CurrentMii).AwaitAnswer();

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

        CurrentMii = selectedMii;
        GameLicenseService.LoadLicense();
        UpdatePage();
        UpdateSidebarProfileIfCurrentUser();
        ViewUtils.ShowSnackbar(t("message_success.mii_changed"));
    }

    private void ViewRoom_OnClick(object? sender, RoutedEventArgs e)
    {
        foreach (var room in RRLiveRooms.Instance.CurrentRooms)
        {
            if (room.Players.All(player => player.FriendCode != currentPlayer?.FriendCode))
                continue;

            NavigationManager.NavigateTo<RoomDetailsPage>(room);
            return;
        }

        MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_CouldNotFindRoom);
    }

    private void CopyFriendCode_OnClick(object? sender, EventArgs e)
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

    private async void RenameMii_OnClick(object? sender, EventArgs e)
    {
        var oldName = CurrentMii?.Name.ToString();
        var extraText = t("question.enter_new_name.extra", oldName ?? string.Empty) ?? string.Empty;
        var renamePopup = new TextInputWindow()
            .SetMainText(t("question.enter_new_name.title"))
            .SetExtraText(extraText)
            .SetAllowCustomChars(true)
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
            ViewUtils.ShowSnackbar(t("snackbar_success.name_change", newName) ?? "Name changed successfully");

        //reload game data, since multiple licenses can use the same mii
        GameLicenseService.LoadLicense();
        UpdatePage();
        UpdateSidebarProfileIfCurrentUser();
    }

    private void UpdateSidebarProfileIfCurrentUser()
    {
        if (FocusedUser != _currentUserIndex)
            return;

        ViewUtils.GetLayout().UpdateSidebarProfile();
    }

    private async Task RefreshVisibleProfileLibraryAsync()
    {
        VisibleProfilesButton.IsVisible = SettingsService.Get<bool>(SettingsService.CLOUD_SYNC_ENABLED);
        if (!VisibleProfilesButton.IsVisible)
            return;

        _allLibraryProfiles = (await ProfileLibraryService.GetAllAsync()).ToList();
        _visibleLibraryProfiles = ProfileLibraryService.GetVisible(_allLibraryProfiles).ToList();
        ApplyVisibleProfileSelectors();
    }

    private void ApplyVisibleProfileSelectors()
    {
        for (var slot = 0; slot < 4; slot++)
        {
            if (RadioButtons.Children[slot] is not RadioButton radio)
                continue;

            if (slot >= _visibleLibraryProfiles.Count)
            {
                radio.Tag = null;
                radio.Content = t("state.no_license");
                radio.IsEnabled = false;
                radio.IsChecked = false;
                continue;
            }

            var profile = _visibleLibraryProfiles[slot];
            radio.Tag = profile;
            radio.Content = TrimProfileSelectorText(profile.Name);
            radio.IsEnabled = true;
            radio.IsChecked =
                profile == _cloudProfileBeingViewed || (profile.LocalSlot == _currentUserIndex && _cloudProfileBeingViewed is null);
        }
    }

    private async void ChooseVisibleProfiles_OnClick(object? sender, RoutedEventArgs e)
    {
        _allLibraryProfiles = (await ProfileLibraryService.GetAllAsync()).ToList();
        var selected = await new ProfileVisibilityWindow()
            .SetProfiles(_allLibraryProfiles, _visibleLibraryProfiles.Select(profile => profile.Key))
            .AwaitAnswer();
        if (selected is null)
            return;

        ProfileLibraryService.SaveVisible(selected);
        _visibleLibraryProfiles = ProfileLibraryService.GetVisible(_allLibraryProfiles).ToList();
        ApplyVisibleProfileSelectors();
        if (
            _visibleLibraryProfiles.Count > 0
            && !_visibleLibraryProfiles.Any(profile =>
                profile.LocalSlot == _currentUserIndex && profile.Source == ProfileLibrarySource.Local
            )
        )
        {
            var first = _visibleLibraryProfiles[0];
            if (first.Source is ProfileLibrarySource.Cloud or ProfileLibrarySource.Vault)
                ShowCloudProfile(first);
            else if (first.LocalSlot is int localSlot)
            {
                _currentUserIndex = localSlot;
                UpdatePage();
            }
        }
    }

    private void ShowCloudProfile(ProfileLibraryEntry profile)
    {
        _cloudProfileBeingViewed = profile;
        SetLocalProfileControlsVisible(false);
        CurrentMii = profile.Mii;
        CurrentFriendCode = profile.FriendCode;
        ProfileAttribUserName.Text = profile.Name;
        ProfileAttribFriendCode.Text = profile.FriendCode;
        ProfileAttribFriendCode.IsVisible = !string.IsNullOrWhiteSpace(profile.FriendCode);
        ProfileAttribVr.Text = profile.Vr == 0 ? "-" : profile.Vr.ToString();
        ProfileAttribBr.Text = profile.Br == 0 ? "-" : profile.Br.ToString();
        ProfileAttribTotalRaces.Text = "-";
        ProfileAttribTotalWins.Text = "-";
        BadgeContainer.Children.Clear();
        IsOnline = false;
        HasCurrentUserRoom = false;
        HasProfileInfo = true;
        CurrentUserProfile.IsVisible = true;
        ProfileCarouselContainer.IsVisible = true;
        UpdateOnlineBorders();
        ApplyVisibleProfileSelectors();
    }

    private void SetLocalProfileControlsVisible(bool visible)
    {
        EditMiiName.IsVisible = visible;
        MiiSelectButton.IsVisible = visible;
        PrimaryCheckBox.IsVisible = visible;
        CopyFcButton.IsVisible = visible;
        ViewRoomButton.IsVisible = visible && HasCurrentUserRoom;
    }

    private void MoveCarouselPage(int offset)
    {
        ActiveInfoSlideIndex += offset;
    }

    private static int NormalizeCarouselIndex(int index)
    {
        var normalized = index % ProfileCarouselPageCount;
        return normalized < 0 ? normalized + ProfileCarouselPageCount : normalized;
    }

    private void UpdateCarouselIndicators()
    {
        SetDotActive(CarouselDot0, ActiveInfoSlideIndex == 0);
        SetDotActive(CarouselDot1, ActiveInfoSlideIndex == 1);
    }

    private static void SetDotActive(Border dot, bool active)
    {
        if (active && !dot.Classes.Contains("active"))
            dot.Classes.Add("active");
        else if (!active && dot.Classes.Contains("active"))
            dot.Classes.Remove("active");
    }

    private static bool IsUserInLiveRoom(string? friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return false;

        return RRLiveRooms.Instance.CurrentRooms.Any(room => room.Players.Any(player => player.FriendCode == friendCode));
    }

    private void UpdateOnlineBorders()
    {
        var outerColor = IsOnline ? ViewUtils.Colors.Primary400 : ViewUtils.Colors.Neutral900;
        var innerColor = IsOnline ? ViewUtils.Colors.Primary400 : ViewUtils.Colors.Neutral600;

        CurrentUserProfile.BorderBrush = new SolidColorBrush(outerColor);
        PART_HeadBorderFace.BorderBrush = new SolidColorBrush(innerColor);
    }

    #region PropertyChanged

    public new event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
